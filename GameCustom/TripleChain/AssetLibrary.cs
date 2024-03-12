#if !UNITY_EDITOR
#define USE_ASSET_BUNDLE
#endif

using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Spine;
using Spine.Unity;
using UniRx.Async;

#if !USE_ASSET_BUNDLE && UNITY_EDITOR
using UnityEditor;
#endif

namespace Eureka
{
    public struct AssetTag
    {
        public string bundleName { get; private set; }
        public string assetName { get; private set; }
        public bool isValid
        {
            get { return (!string.IsNullOrEmpty(bundleName) && !string.IsNullOrEmpty(assetName)); }
        }

        public AssetTag(string bundleName, string assetName)
        {
            this.bundleName = (string.IsNullOrEmpty(bundleName) || bundleName.Equals("null")) ? null : bundleName;
            this.assetName = (string.IsNullOrEmpty(assetName) || assetName.Equals("null")) ? null : assetName;
        }

        public static AssetTag FromName(string bundleName, string assetName)
        {
            var tag = new AssetTag();
            tag.bundleName = bundleName;
            tag.assetName = assetName;

            return tag;
        }
    }

    public sealed class AssetTagComparer : IEqualityComparer<AssetTag>
    {
        public bool Equals(AssetTag x, AssetTag y)
        {
            return string.Equals(x.bundleName, y.bundleName) && string.Equals(x.assetName, y.assetName);
        }

        public int GetHashCode(AssetTag type)
        {
            return (type.bundleName != null ? type.bundleName.GetHashCode() : 0) ^ (type.assetName != null ? type.assetName.GetHashCode() : 0);
        }
    }

	public sealed class BundleBox
	{
		public AssetBundle bundle   { get; private set; }		

		private BundleBox()
		{	
		}

		public static BundleBox FromAssetBundle(AssetBundle bundle)
		{
			if(bundle == null)
			{
				return null;
			}

			var bundleBox	= new BundleBox();
			bundleBox.bundle	= bundle;

			return bundleBox;
		}

		// 동기 애셋 로드.
		public Object LoadAsset(string assetName)
		{
			return bundle.LoadAsset(assetName);
		}

		// 비동기 애셋 로드.
		public AssetBundleRequest LoadAssetAsync(string assetName)
		{
			return bundle.LoadAssetAsync(assetName);
		}

		public void Unload()
		{
			if(bundle != null)
			{
				bundle.Unload(true);
			}
			bundle    = null;
		}
	}

	/*
	 * AseetBundle 및 Asset 관리 클래스.
	*/
	public static class AssetLibrary
	{
        // extra resource
        public static readonly string introVideo = "intro_video.mp4";
        public static string pathIntroVideo
        {
            get
            {
                var path = Path.Combine(AssetBundleDownloader.bundlesFolderPath, introVideo);
                if (File.Exists(path))
                {
#if UNITY_IOS                                        
                    path = "file://" + path;
#endif
                }
                else
                {
                    // streaming asset 내부에 애셋번들 폴더 안에 있으므로 bundle root 폴더와 combine.
                    path = Path.Combine(AssetBundleDownloader.BUNDLE_ROOT_NAME, introVideo);
                }

                return path;
            }
        }

        private static bool		_isInitialized;
		private static AssetBundle			_rootBundle;
		private static AssetBundleManifest	_rootManifest;
		private static Dictionary<string, BundleBox>	_loadedBoxSet;                

        // 비동기 로드 관련 변수들.
        private static HashSet<AssetTag> allAsyncLoadSet = new HashSet<AssetTag>(new AssetTagComparer());
        public static int  totalAsyncSetCount { get { return allAsyncLoadSet.Count; } }

        static AssetLibrary()
		{
            OnSystemRestart();
		}

        public static void OnSystemRestart()
        {
            UnloadAll();

            _isInitialized = false;
            _rootBundle = null;
            _rootManifest = null;
            _loadedBoxSet = null;
        }

        public static void Initialize()
		{
			if(!_isInitialized)
			{
                _loadedBoxSet = new Dictionary<string, BundleBox>();
                _isInitialized = true;

				// 초기에 무조건 포함되어 사용해야하는 애셋번들 로드.
			}
		}

		public static async UniTask ReadyAsync(bool forceRefresh = true)
		{
#if USE_ASSET_BUNDLE
			if(!forceRefresh && _rootBundle != null)
			{
				return;
			}

            StopWatchCustom.instance.StartWatch(StopWatchPoint.AssetLibrary_Ready);

            if (_rootBundle != null)
			{
                _rootManifest = null;
                _rootBundle.Unload(true);
                _rootBundle = null;
			}

            // 전체 manifest를 담고 있는 root assetbundle을 로드.
            _rootBundle = await LoadFromFileAsync(AssetBundleDownloader.BUNDLE_ROOT_NAME);
            if (_rootBundle != null)
            {
                var request = _rootBundle.LoadAssetAsync<AssetBundleManifest>("AssetBundleManifest");
                await request;
                _rootManifest = request.asset as AssetBundleManifest;
            }

            StopWatchCustom.instance.StopWatch(StopWatchPoint.AssetLibrary_Ready);
#else
            await UniTask.DelayFrame(1);    // 에디터에서 실행시 await 가 없으니 함수명에 잠재적 수정사항이 표시되서 추가
#endif
        }

        public static void UnloadAll()
        {
            if (_loadedBoxSet != null)
            {
                foreach (var elem in _loadedBoxSet)
                {
                    elem.Value.Unload();
                }
                _loadedBoxSet.Clear();
            }

            if (_rootBundle != null)
            {
                _rootBundle.Unload(true);
                _rootBundle = null;
            }
        }

		public static T Load<T>(AssetTag info) where T : Object
		{			
			if(!info.isValid)
			{
				return null;
			}

			return Load<T>(info.bundleName, info.assetName);
		}

		/*
		 * assetName은 이름이 유니크하면 이름만 사용가능하고, 중복된 이름이 있으면 전체 path를 전달하면 로드할 수 있다.
		*/
		public static T Load<T>(string bundleName, string assetName) where T : Object
		{	
			if(!_isInitialized || string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(assetName))
			{
				return null;
			}

#if USE_ASSET_BUNDLE
            var assetObject	= LoadFromBundle(bundleName, assetName);
            ProcessSkeletonData(assetObject, true);
#else
            var assetObject = LoadFromAssetDB(bundleName, assetName);
            ProcessSkeletonData(assetObject, false);
#endif

            T asset = null;
            if (assetObject is T)
			{
                asset = assetObject as T;
			}
			else
			{				
				var go	= assetObject as GameObject;
                asset = go?.GetComponentInChildren<T>();
			}

            return asset;
		}

#if !USE_ASSET_BUNDLE && UNITY_EDITOR
        private static Object LoadFromAssetDB(string bundleName, string assetName)
		{
			Object asset	= null;
			string path		= null;
			var assetPaths	= AssetDatabase.GetAssetPathsFromAssetBundleAndAssetName(bundleName, assetName);

			if(assetPaths.Length == 1)
			{		
				// assetName이 유니크해서 assetPaths가 하나라고 가정한다. 
				path	= assetPaths[0];
			}
			else
            {
                // 번들이름과 애셋이름으로 검색되지 않으면 assetName이 경로를 포함한다고 가정한다.
                path	= assetName;
			}

            asset = AssetDatabase.LoadMainAssetAtPath(path);

			return asset;
		}
#endif

        private static Object LoadFromBundle(string bundleName, string assetName)
		{
			Object asset	= null;
			var bundleBox	= GetAssetBundleBox(bundleName);
			if(bundleBox != null)
			{	
				asset		= bundleBox.LoadAsset(assetName);
			}

			return asset;
		}

		public static BundleBox GetAssetBundleBox(string bundleName)
		{	
			if(_loadedBoxSet.ContainsKey(bundleName))
			{
				return _loadedBoxSet[bundleName];
			}

			var assetBundle	= LoadAssetBundle(bundleName);
			var bundleBox	= AddAssetBundleBox(bundleName, assetBundle);
			if(bundleBox == null)
			{
				return null;
			}

			if(!LoadAllDependencyBundles(bundleName))
			{
				return null;
			}

			return bundleBox;
		}

		private static BundleBox AddAssetBundleBox(string bundleName, AssetBundle assetBundle)
		{			
			if(string.IsNullOrEmpty(bundleName) || assetBundle == null)
			{
				return null;
			}

			if(_loadedBoxSet.ContainsKey(bundleName))
			{
				return _loadedBoxSet[bundleName];
			}	

			var bundleBox	= BundleBox.FromAssetBundle(assetBundle);
			if(bundleBox != null)
			{
				_loadedBoxSet.Add(bundleName, bundleBox);
			}

			return bundleBox;
		}

		private static AssetBundle LoadAssetBundle(string bundleName)
		{
            var fullPath = AssetBundleDownloader.GetBundleFilePath(bundleName);
            if(string.IsNullOrEmpty(fullPath))
            {
                return null;
            }
            else
            {
                return AssetBundle.LoadFromFile(fullPath);
            }
		}

		private static bool LoadAllDependencyBundles(string bundleName)
		{
			if(_rootManifest == null)
			{
				return false;
			}

			var isSuccess	= true;
			var allDependencies	= _rootManifest.GetAllDependencies(bundleName);
			for(int i = 0, count = allDependencies.Length; i < count; i++)
			{
				var name	= allDependencies[i];
				if(!_loadedBoxSet.ContainsKey(name))
				{
					var assetBundle	= LoadAssetBundle(name);
					if(AddAssetBundleBox(name, assetBundle) == null)					
					{
						isSuccess	= false;
					}
				}
			}

			return isSuccess;
		}

        //========================= 비동기 로드 관련 함수들 ============================//
        public static void AddToAsyncSet(AssetTag tag)
        {
            if(!tag.isValid)
            {
                return;
            }

            allAsyncLoadSet.Add(tag);
        }

        public static void AddToAsyncSet(List<AssetTag> tagList)
        {
            if (tagList == null || tagList.Count <= 0)
            {
                return;
            }

            foreach (var tag in tagList)
            {
                if (!tag.isValid)
                {
                    continue;
                }

                allAsyncLoadSet.Add(tag);
            }
        }

        public static async UniTask LoadAllAsync(List<AssetTag> tagList = null)
        {
            AddToAsyncSet(tagList);

            if (allAsyncLoadSet.Count <= 0)
            {
                return;
            }

            Debug.Log("Start Async Load Asset, Count: " + allAsyncLoadSet.Count);

            foreach (var tag in allAsyncLoadSet)
            {
                await LoadAsync<Object>(tag.bundleName, tag.assetName);
                EventMessenger.Send(EventName.completeLoadOneTask);
            }

            Debug.Log("Finish Async Load Asset, Count: " + allAsyncLoadSet.Count);
            allAsyncLoadSet.Clear();
        }

        public static async UniTask<T> LoadAsync<T>(AssetTag info, System.Action<T> cbComplete = null) where T : Object
        {
            return await LoadAsync<T>(info.bundleName, info.assetName, cbComplete);            
        }

        public static async UniTask<T> LoadAsync<T>(string bundleName, string assetName, System.Action<T> cbComplete = null) where T : Object
        {
            if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(assetName))
            {
                cbComplete?.Invoke(null);
                return null;
            }

#if USE_ASSET_BUNDLE
            var assetObject = await LoadFromBundleAsync(bundleName, assetName);
            await ProcessSkeletonDataAsync(assetObject, true);
#else
            var assetObject = await LoadFromAssetDBAsync(bundleName, assetName);
            await ProcessSkeletonDataAsync(assetObject, false);
#endif

            T asset = null;
            if (assetObject is T)
            {
                asset = assetObject as T;
            }
            else
            {
                var go = assetObject as GameObject;
                asset = go?.GetComponentInChildren<T>();
            }

            cbComplete?.Invoke(asset);
            return asset;
        }

#if !USE_ASSET_BUNDLE && UNITY_EDITOR
        private static async UniTask<Object> LoadFromAssetDBAsync(string bundleName, string assetName)
        {
            int delayMS = Random.Range(10, 50);
            await UniTask.Delay(delayMS);

            return LoadFromAssetDB(bundleName, assetName);
        }
#endif

        private static async UniTask<Object> LoadFromBundleAsync(string bundleName, string assetName)
        {
            BundleBox bundleBox = await GetAssetBundleBoxAsync(bundleName);

            // assetBox가 있으면 비동기 asset 로드 실행. 없으면 null 리턴.
            Object asset = null;
            if (bundleBox != null)
            {
                var request = bundleBox.LoadAssetAsync(assetName);
                await request;

                if (request.asset != null)
                {
                    asset = request.asset;
                }
            }

            return asset;
        }

        public static async UniTask<BundleBox> GetAssetBundleBoxAsync(string bundleName)
        {
            BundleBox bundleBox = null;
            if (!_loadedBoxSet.TryGetValue(bundleName, out bundleBox) && _rootManifest != null)
            {
                // dependency 번들을 포함하여 필요한 AssetBundle 로드.
                var allDependencies = _rootManifest.GetAllDependencies(bundleName);
                foreach (string dependName in allDependencies)
                {
                    await LoadAssetBundleAsync(dependName);
                }
                bundleBox = await LoadAssetBundleAsync(bundleName);
            }
            return bundleBox;
        }

        private static async UniTask<BundleBox> LoadAssetBundleAsync(string bundleName)
        {
            BundleBox bundleBox = null;
            if (!_loadedBoxSet.TryGetValue(bundleName, out bundleBox))
            {
                var assetBundle = await LoadFromFileAsync(bundleName);
                if (assetBundle != null)
                {
                    bundleBox = AddAssetBundleBox(bundleName, assetBundle);
                }
                else
                {
                    // assetbundle이 null인 경우는 파일이 없거나 이미 로드된 경우이다.
                    _loadedBoxSet.TryGetValue(bundleName, out bundleBox);
                }
            }
            return bundleBox;
        }

        private static async UniTask<AssetBundle> LoadFromFileAsync(string bundleName)
        {
            var fullPath = AssetBundleDownloader.GetBundleFilePath(bundleName);
            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }
            else
            {
                var request = AssetBundle.LoadFromFileAsync(fullPath);
                await request;
                return request.assetBundle;
            }
        }

        //================== 스파인 스켈레톤 data 비동기 로드 함수 ===========================//
        private static void ProcessSkeletonData(Object asset, bool loadFromBundle)
        {
            var go = asset as GameObject;
            if (go != null)
            {
                var animations = go.GetComponentsInChildren<SkeletonAnimation>(true);
                foreach (var animation in animations)
                {
                    ReadSkeletonData(animation.SkeletonDataAsset, loadFromBundle);
                }
            }
            else
            {
                var skeletonAsset = asset as SkeletonDataAsset;
                if (skeletonAsset != null)
                {
                    ReadSkeletonData(skeletonAsset, loadFromBundle);
                }
            }
        }

        private static void ReadSkeletonData(SkeletonDataAsset asset, bool loadFromBundle)
        {
            if (asset == null || asset.IsLoaded || asset.skeletonJSON == null)
            {
                return;
            }

            var isBinary = asset.skeletonJSON.name.ToLower().Contains(".skel");
            var scale = asset.scale;
            var atlasArray = asset.GetAtlasArray();
            var loader = (atlasArray.Length == 0) ?
                (AttachmentLoader)new RegionlessAttachmentLoader() :
                (AttachmentLoader)new AtlasAttachmentLoader(atlasArray);

            SkeletonData data = null;
            if (isBinary)
            {
                var bytes = asset.skeletonJSON.bytes;
                data = SkeletonDataAsset.ReadSkeletonData(bytes, loader, scale);
            }
            else
            {
                var text = asset.skeletonJSON.text;
                data = SkeletonDataAsset.ReadSkeletonData(text, loader, scale);
            }

            if (!asset.IsLoaded)
            {
                asset.InitializeWithData(data);
            }

            if (loadFromBundle)
            {
                asset.skeletonJSON = null;
            }
        }

        private static async UniTask ProcessSkeletonDataAsync(Object asset, bool loadFromBundle)
        {
            var go = asset as GameObject;
            if (go != null)
            {
                var animations = go.GetComponentsInChildren<SkeletonAnimation>(true);
                foreach (var animation in animations)
                {
                    await ReadSkeletonDataAsync(animation.SkeletonDataAsset, loadFromBundle);
                }
            }
            else
            {
                var skeletonAsset = asset as SkeletonDataAsset;
                if (skeletonAsset != null)
                {
                    await ReadSkeletonDataAsync(skeletonAsset, loadFromBundle);
                }
            }
        }

        private static async UniTask ReadSkeletonDataAsync(SkeletonDataAsset asset, bool loadFromBundle)
        {
            if (asset == null || asset.IsLoaded || asset.skeletonJSON == null)
            {
                return;
            }

            var isBinary = asset.skeletonJSON.name.ToLower().Contains(".skel");
            var scale = asset.scale;
            var atlasArray = asset.GetAtlasArray();
            var loader = (atlasArray.Length == 0) ?
                (AttachmentLoader)new RegionlessAttachmentLoader() :
                (AttachmentLoader)new AtlasAttachmentLoader(atlasArray);

            SkeletonData data = null;
            if (isBinary)
            {
                var bytes = asset.skeletonJSON.bytes;
                data = await UniTask.Run(() => SkeletonDataAsset.ReadSkeletonData(bytes, loader, scale));
            }
            else
            {
                var text = asset.skeletonJSON.text;
                data = await UniTask.Run(() => SkeletonDataAsset.ReadSkeletonData(text, loader, scale));
            }

            if (!asset.IsLoaded)
            {
                asset.InitializeWithData(data);
            }

            if (loadFromBundle)
            {
                asset.skeletonJSON = null;
            }
        }

        //================== Load Scene ===========================//
        public static async UniTaskVoid LoadSceneAsync(string bundleName, string sceneName, LoadSceneMode mode)
        {
            EventMessenger.SetDelayMode(true);

#if USE_ASSET_BUNDLE
            BundleBox bundleBox = await GetAssetBundleBoxAsync(bundleName);
            if (bundleBox != null && bundleBox.bundle.isStreamedSceneAssetBundle)
            {
                foreach (string scenePath in bundleBox.bundle.GetAllScenePaths())
                {
                    if (scenePath.Contains(sceneName))
                    {
                        sceneName = scenePath;
                        break;
                    }
                }
            }
#endif

            await SceneManager.LoadSceneAsync(sceneName, mode);
        }
    }
}


