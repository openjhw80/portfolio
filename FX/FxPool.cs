using UnityEngine;
using UnityEngine.AddressableAssets;


namespace Seeder
{
    public sealed class FxPool : CompPoolBase<FxHandler>
    {
        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================
        public static FxPool FromSetting(CompPoolSetting setting)
        {
            if (!setting.IsValid) return null;

            var pool = new FxPool();
            pool.Initialize(setting);            

            return pool;
        }

        //================================================================================
        // override 메서드 모음 
        //================================================================================    
        protected override void OnInstantiate(FxHandler item)
        {
            if (item == null) return;

            item.Id = Id;            
        }

        protected override void OnReadyToLend(FxHandler item)
        {
            if (item == null) return;

            item.SetReturnCB(Return);
        }

        protected override void OnReadyToReturn(FxHandler item)
        {
            if (item == null) return;

            item.Stop(true);
            base.OnReadyToReturn(item);
        }

        //================================================================================
        // 새로 정의한 일반 메서드 모음./
        //================================================================================      

    }
}
