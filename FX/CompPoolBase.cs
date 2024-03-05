using UnityEngine;
using UnityEngine.AddressableAssets;


namespace Seeder
{
    public struct CompPoolSetting
    {
        public string Id { get; set; }
        public string AddressableName { get; set; }
        public int InitialCount { get; set; }
        public int AddCount { get; set; }
        public Transform TfParent { get; set; }
        public bool IsValid { get { return !string.IsNullOrEmpty(AddressableName); } }            
    }

    public abstract class CompPoolBase<T> : ObjectPoolBase<T> where T : Component
    {
        protected int _index = 0;
        protected int _addCount = 2;
        protected string _addressableName;
        protected Transform _tfPool;

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================

        //================================================================================
        // override 메서드 모음 
        //================================================================================  
        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            Object.Destroy(_tfPool.gameObject);
            _tfPool = null;
        }

        public override void CreateItem(int targetCount = 0)
        {
            var createCount = targetCount > 0 ? targetCount : _addCount;
            if(createCount <= 0 || string.IsNullOrEmpty(_addressableName))
            {
                return;
            }
            
            for (int i = 0; i < createCount; i++)
            {
                // prefab instantiate.                
                Addressables.InstantiateAsync(_addressableName).Completed += (handler) =>
                {
                    handler.AddTo(this);

                    var comp = handler.Result.GetComponent<T>();                            
                    if (comp != null)
                    {
                        comp.name += "_" + _index++.ToString();
                        comp.transform.SetParent(_tfPool);
                        comp.transform.localPosition = Vector3.zero;
                        Add(comp);

                        OnInstantiate(comp);
                    }
                    else
                    {
                        Debug.LogError("CompPool.CreateItem() InstantiateAsync Failed!!: " + _addressableName);
                    }
                };
            }
        }

        protected override void OnRemoveItem(T item)
        {
            if (item == null) return;
            Object.Destroy(item.gameObject);
        }

        protected override void OnReadyToReturn(T item)
        {
            if (item == null) return;

            item.gameObject.SetActive(false);            
            item.transform.SetParent(_tfPool);
            item.transform.localPosition = Vector3.zero;
        }

        //================================================================================
        // 새로 정의한 일반 메서드 모음./
        //================================================================================
        protected void Initialize(CompPoolSetting setting)
        {
            Id = setting.Id != null ? setting.Id : string.Empty;
            _addressableName = setting.AddressableName;
            _addCount = setting.AddCount;
            if(_tfPool == null)
            {
                var name = "pool_" + (string.IsNullOrEmpty(Id) ? _addressableName : Id);
                _tfPool = new GameObject(name).transform;
                _tfPool.position = Values.HidePostion;
            }
            _tfPool.SetParent(setting.TfParent);

            CreateItem(setting.InitialCount);
        }

        public void SetParent(Transform parent)
        {
            _tfPool.SetParent(parent);
        }

        protected virtual void OnInstantiate(T comp) { }
    }
}
