using UniRx;


namespace Seeder
{
    public sealed class UnitIdleAction : ActorAction
    {
        private enum EIdleType { Normal, Tired }

        private PercentChecker _hpUnderCondition;
        private EIdleType _idleType = EIdleType.Normal;        

        //================================================================================
        // 기초 메서드(생성자, 초기화등) 모음
        //================================================================================  
        private UnitIdleAction() { }
        public static UnitIdleAction FromParams(ActionInitParams setting)
        {
            if (setting.Actor == null)
            {
                return null;
            }

            setting.Category = ActionCategory.idle;
            if (setting.Name.IsNullOrEmpty())
                setting.Name = ActionName.unit_idle;

            var instance = new UnitIdleAction();
            instance.Initialize(setting);

            return instance;
        }

        protected override void OnInitialize()
        {
            var status = Actor.Role.Stats;
            // 현재 hp 비율 조건 생성.            
            _hpUnderCondition = PercentChecker.FromStream(
                status.HpNow, status.Hp,
                status.OnUpdateStatAsObservable(StatName.hp_now),
                status.OnUpdateStatAsObservable(StatName.hp));
            _hpUnderCondition.Condition = new NumericCondition<double>()
            {
                Value = 30d.ToFraction(), // 체력이 30퍼이하면 조건 발동.
                Now = 1d,
                CompareType = ECompareType.LessOrEqual,
            };
            _hpUnderCondition.OnUpdateConditionAsObservable()
                .Subscribe(_ => OnUpdateHpUnderCondition(_hpUnderCondition))
                .AddTo(this);
        }

        //================================================================================
        // override 메서드 모음 
        //================================================================================
        protected override void OnDispose(bool disposing)
        {
            _hpUnderCondition?.Dispose();
        }

        protected override void OnStart()
        {
            PlayAnimation();
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================ 
        private string GetAniNameByType()
        {
            return _idleType switch
            {
                EIdleType.Tired => AniName.idle_tired,
                _ => AniName.idle_normal,
            };
        }

        private void PlayAnimation()
        {
            Actor.Look.AniPlayer.Play(new AniPlayData(){ AniName = GetAniNameByType(), Loop = true });
        }

        //================================================================================
        // 이벤트 및 콜백 처리
        //================================================================================
        private void OnUpdateHpUnderCondition(PercentChecker checker)
        {
            _idleType = checker.IsMatch ? EIdleType.Tired : EIdleType.Normal;            
            if (!IsActive || Actor.Look.AniPlayer.AnimationName == GetAniNameByType())
            {
                return;
            }

            PlayAnimation();
        }
    }
}
