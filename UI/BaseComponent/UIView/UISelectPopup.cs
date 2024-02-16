using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UniRx;


namespace Seeder
{
    // 공통으로 사용하는 선택, 취소 팝업 UI.
    public sealed class UISelectPopup : UIView
    {
        public static string ViewId { get { return UIName.ui_select_popup; } }        

        public struct OpenParam
        {   
            public string Msg { get; set; }
            public Action<UISelectPopup> CbClickYes { get; set; }
            public Action CbClickNo { get; set; }
        }
        
        [SerializeField] private UIText _uiMsg;        
        [SerializeField] private UIBlock _btnNo;
        [SerializeField] private UIBlock _btnYes;        

        private OpenParam _openParam;

        //================================================================================
        // MonoBehavior 메서드 모음
        //================================================================================                
        protected override void OnAwake()
        {
            base.OnAwake();

            if (_orderLayer == UIViewOrderLayer.None)
            {
                _orderLayer = UIViewOrderLayer.Default;
            }

            // 버튼 설정.            
            _btnNo.OnClickAsObservable()
                .Subscribe(_ => ClickNo())
                .AddTo(this);
            _btnYes.OnClickAsObservable()
                .Subscribe(_ => ClickYes())
                .AddTo(this);
        }

        //================================================================================
        // override 메서드 모음 
        //================================================================================       
        protected override void OnOpen<T>(T data)
        {            
            var param = data as OpenParam?;
            if (param != null)
            {
                _openParam = param.Value;
            }
            else
            {
                _openParam = default;
            }
        }

        protected override void OnClose<T>(T data)
        {
            _openParam = default;
        }

        protected override void SetData()
        {
            _uiMsg.SetText(_openParam.Msg);
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================  
        private void ClickYes()
        {
            // close에 파라미터가 초기화 되므로 미리 복사한다.
            var openParam = _openParam;
            Close();

            openParam.CbClickYes?.Invoke(this);
        }

        private void ClickNo()
        {
            // close에 파라미터가 초기화 되므로 미리 복사한다.
            var openParam = _openParam;
            Close();

            openParam.CbClickNo?.Invoke();
        }

        //================================================================================
        // 이벤트 및 콜백 처리
        //================================================================================ 
    }
}
