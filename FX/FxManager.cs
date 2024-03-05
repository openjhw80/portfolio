using System.Collections.Generic;
using UnityEngine;


namespace Seeder
{
    // 전체 Effect 등록 및 관리를 위한 클래스
    public sealed class FxManager : SystemManager
    {
        private Dictionary<string, FxPool> _fxPoolDic = new Dictionary<string, FxPool>();
        private Transform _tfRoot;

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================  
        private FxManager()
        {            
        }

        public static FxManager Create()
        {
            var mgr = SystemCenter.FxMgr;
            if (mgr == null)
            {
                mgr = new FxManager()
                {
                    Name = SystemName.effect,
                };
                mgr.Register(); // Initialize에서 접근할수 있으므로 가장 먼저 등록해야한다.
                mgr.Initialize();
            }

            return mgr;
        }

        //================================================================================
        // override 메서드 모음 
        //================================================================================        
        protected override void OnDispose(bool disposing)
        {   
            foreach(var elem in _fxPoolDic)
            {
                elem.Value.Dispose();
            }
            _fxPoolDic.Clear();

            Object.Destroy(_tfRoot.gameObject);
            _tfRoot = null;            
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        private void Initialize()
        {
            _tfRoot = new GameObject("EffectPool").transform;            
            Object.DontDestroyOnLoad(_tfRoot.gameObject);

            // fx 데이터 라이브러리에서 초기 count값이 있는 fx를 로드.
            var fxSet = DataCenter.GetDLValues(DLName.fx);
            foreach (var fxDl in fxSet)
            {
                var initCount = fxDl.GetInt(AttrName.count_init);
                if (initCount <= 0)
                {
                    continue;
                }

                CreatePool(fxDl);
            }            
        }

        private void CreatePool(GameDL fxDl, int initCount = 0)
        {
            var fxName = fxDl == null ? string.Empty : fxDl.GetString(AttrName.name);
            if (fxName.IsNullOrEmpty() || _fxPoolDic.ContainsKey(fxName))
            {
                return;
            }

            var tableInitCount = fxDl.GetInt(AttrName.count_init);
            tableInitCount = tableInitCount <= 0 ? 1 : tableInitCount;
            var setting = new CompPoolSetting()
            {
                Id = fxDl.GetString(AttrName.name),
                AddressableName = fxDl.GetString(AttrName.addressable),
                InitialCount = initCount > tableInitCount ? initCount : tableInitCount,
                AddCount = fxDl.GetInt(AttrName.count_add),
                TfParent = _tfRoot,
            };
            var pool = FxPool.FromSetting(setting);
            _fxPoolDic.Add(pool.Id, pool);
        }

        private void CreatePool(string name, int initCount = 0)
        {
            CreatePool(DataCenter.GetDL(DLName.fx, name), initCount);
        }

        // ifNotEnough가 true면 부족할때만 추가한다.
        public void Add(string name, int count = 0, bool ifNotEnough = true)
        {
            if (name.IsNullOrEmpty())
            {
                return;
            }

            if(!_fxPoolDic.ContainsKey(name))
            {
                CreatePool(name, count);
                return;
            }
            
            if (ifNotEnough)
            {
                var usableCount = _fxPoolDic[name].CountUsable;
                var needCount = count - usableCount;
                if (needCount > 0)
                {
                    _fxPoolDic[name].CreateItem(needCount);
                }
            }
            else
            {
                _fxPoolDic[name].CreateItem(count);
            }
        }

        public FxHandler Get(string name)
        {
            if (name.IsNullOrEmpty())
            {                
                return null;
            }
            if (!_fxPoolDic.ContainsKey(name))
            {
                Debug.LogError("EffectPool.Get(). pool is not exist." + name);
                // pool 생성은 시켜두고 null을 반환. 다음부터는 사용가능.
                CreatePool(name, 2);
                return null;
            }

            var pool = _fxPoolDic[name];
            if (pool.CountUsable == 0)
            {
                Debug.LogError("EffectPool.Get(). usable count is 0." + name);
                // 추가 이펙트 생성은 시켜두고 null을 반환. 다음부터는 사용가능.
                pool.CreateItem(2);
                return null;
            }

            return pool.Lend();
        }

        public void Return(FxHandler handler)
        {
            if (handler == null)
            {
                return;
            }

            var name = handler.Id;
            if (name.IsNullOrEmpty() || !_fxPoolDic.ContainsKey(name))
            {
                Debug.LogError("EffectPool.Return() is NOT Exist!!! : " + name);
                return;
            }

            _fxPoolDic[name].Return(handler);
        }
    }
}