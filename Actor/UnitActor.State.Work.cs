using System.Collections.Generic;


namespace Seeder
{
    // Unit Actor의 state 데이터 설정.
    public sealed partial class UnitActor
    {
        //======= state 구성 ======//       
        private void AddWorkState()
        {
            var state = AddState(UnitWorkState.FromParams, new StateInitParams() { Actor = this });
            if (state != null)
            {
                // 출격.
                state.AddToWhiteList(AddLaunchAction());                
                // 움찔거림.
                state.AddToWhiteList(AddFlinchAction());                

                // idle.
                state.AddToWhiteList(AddIdleAction());
                // 이동.
                state.AddToWhiteList(AddMoveAction());
                // 공격.
                state.AddToWhiteList(AddAttackAction());
                // 연타.
                state.AddToWhiteList(AddAttackExtraAction());
                // 스킬.
                state.AddToWhiteList(AddSkillAction());                
            }
        }

        //======= action 구성 ======//
        // launch action 구성.
        private ActorAction AddLaunchAction()
        {
            var actionName = ActionName.unit_launch;
            if (_actionSet.TryGetValue(actionName, out var action))
            {
                return action;
            }

            var initParams = new ActionInitParams()
            {
                Actor = this,
                Category = ActionCategory.appear,
                Name = actionName,
                AniDataList = new List<AniPlayData>(),
                NextAction = ActionCategory.idle,
            };
            var aniData = new AniPlayData()
            {
                AniName = AniName.battle_in,
                ReplayForce = true,
            };
            initParams.AniDataList.Add(aniData);

            return AddAction(DefaultAction.FromParams, initParams);
        }        
        // flinch action 구성.
        private ActorAction AddFlinchAction()
        {
            var actionName = ActionName.unit_flinch;
            if (_actionSet.TryGetValue(actionName, out var action))
            {
                return action;
            }

            var initParams = new ActionInitParams()
            {
                Actor = this,
                Category = ActionCategory.flinch,
                Name = actionName,
                AniDataList = new List<AniPlayData>(),
                NextAction = ActionCategory.idle,
            };
            initParams.AniDataList.Add(new AniPlayData() { AniName = AniName.damage, ReplayForce = true });

            return AddAction(DefaultAction.FromParams, initParams);
        }

        // idle action 구성.
        private ActorAction AddIdleAction()
        {
            var actionName = ActionName.unit_idle;
            if (_actionSet.TryGetValue(actionName, out var action))
            {
                return action;
            }

            var initParams = new ActionInitParams()
            {
                Actor = this,                
                Name = actionName,                
            };
            return AddAction(UnitIdleAction.FromParams, initParams);
        }

        // move action 구성.
        private ActorAction AddMoveAction()
        {
            var actionName = ActionName.unit_move;
            if (_actionSet.TryGetValue(actionName, out var action))
            {
                return action;
            }

            var initParams = new ActionInitParams()
            {
                Actor = this,
                Category = ActionCategory.move,
                Name = actionName,
                AniDataList = new List<AniPlayData>(),
            };
            initParams.AniDataList.Add(new AniPlayData() { AniName = AniName.move });

            return AddAction(DefaultAction.FromParams, initParams);
        }

        // attack action 구성.
        private ActorAction AddAttackAction()
        {
            var actionName = ActionName.unit_attack;
            if (_actionSet.TryGetValue(actionName, out var action))
            {
                return action;
            }

            var initParams = new ActionInitParams()
            {
                Actor = this,
                Name = actionName,
            };
            return AddAction(UnitAttackAction.FromParams, initParams);
        }
        // attack extra action 구성.
        private ActorAction AddAttackExtraAction()
        {
            var actionName = ActionName.unit_attack_extra;
            if (_actionSet.TryGetValue(actionName, out var action))
            {
                return action;
            }

            var initParams = new ActionInitParams()
            {
                Actor = this,
                Name = actionName,
            };
            return AddAction(UnitAttackExtraAction.FromParams, initParams);
        }
        // skill action 구성.
        private ActorAction AddSkillAction()
        {
            var actionName = ActionName.unit_skill;
            if (_actionSet.TryGetValue(actionName, out var action))
            {
                return action;
            }

            if (Unit == null)
            {
                return null;
            }

            var initParams = new ActionInitParams()
            {
                Actor = this,
                Name = actionName,
            };
            var skillReload = SkillActionDefault.FromParams(initParams, Unit.SkillReload);
            TryAddAction(skillReload);

            return skillReload;
        }
    }
}
