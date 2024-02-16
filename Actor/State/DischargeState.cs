using UniRx;


namespace Seeder
{
    // 영웅 유닛의 배터리 방전에 대한 상태 처리 클래스.
    public sealed class DischargeState : ActorState
    {   
        private double _batteryChargeNow;

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================
        private DischargeState() { }
        public static DischargeState FromParams(StateInitParams setting)
        {
            if (setting.Actor == null)
            {
                return null;
            }

            setting.Name = StateName.discharge;
            setting.StartAction = ActionCategory.@in;            

            var instance = new DischargeState();
            instance.Initialize(setting);

            return instance;
        }

        protected override void OnInitialize()
        {
            //====== 방전 상태 진입 체크 설정 ======//
            Observable.EveryUpdate()
                .Where(_ => !IsRun)
                .Subscribe(_ =>
                {
                    // idle상태일때, 배터리가 0이하로 갈 때만 상태전환을 한다.
                    if (Actor.ActionNowCategory != ActionCategory.idle
                    || Actor.Role.Stats.Battery <= 0
                    || Actor.Role.Stats.BatteryNow > 0)
                    {
                        return;
                    }

                    if (!Actor.ExistStateNow(StateName.discharge))
                    {
                        Actor.SetState(StateName.discharge);
                    }
                })
                .AddTo(this);

            //====== 충전 로직 설정 ======//
            // 시간 흐름에 따른 충전.
            Observable.EveryUpdate()
                .Where(_ => IsRun)
                .Subscribe(_ =>
                {
                    var chargeValue = TimeTool.DeltaTime * (1d + Actor.Role.Stats.BatteryHaste.ToFraction());
                    ChageBattery(chargeValue);

                    // 배터리 충전이 완료되면 상태 종료.
                    if (Actor.Role.Stats.Battery <= Actor.Role.Stats.BatteryNow)
                    {
                        // 현재 상태 종료.
                        Actor.RemoveState(Name);
                    }
                })
                .AddTo(this);

            // 유닛 터치에 따른 충전.
            Actor.OnTouchEventAsObservable()
                .Where(evtData => IsRun && evtData.State == EPointerState.Click)
                .Subscribe(_ =>
                {
                    // 터치에 대한 배터리 충전량 추가.
                    var chargeTime = Actor.Role.Stats.BatteryCharge;
                    var bonusRate = Actor.Role.Stats.BatteryTouch.ToFraction();
                    var chargeValue = chargeTime * bonusRate;
                    ChageBattery(chargeValue);

                    // fx 표시.
                    Actor.Look.ShowEffect(FxName.touch_charge);
                    Actor.Look.AniPlayer.PlaySubEffect(EAniSubEffect.Reaction);
                })
                .AddTo(this);
        }

        //================================================================================
        // override 메서드 모음 
        //================================================================================
        protected override void OnStart()
        {   
            _batteryChargeNow = 0;
        }
 
        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        private void ChageBattery(double chargeValue)
        {   
            _batteryChargeNow += chargeValue;

            var statCenter = Actor.Role.Stats;
            statCenter.BatteryNow = statCenter.Battery * (_batteryChargeNow / statCenter.BatteryCharge);
        }
    }
}
