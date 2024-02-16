using UnityEngine;
using UnityEngine.UI;
using TMPro;


namespace Seeder
{
    // Text를 표시하는 UI 기능.    
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class UIText : UIAdaptor
    {        
        private TextMeshProUGUI _core;

        // 기본 변수들.
        public TextMeshProUGUI Core
        { 
            get
            {
                // [220523][jhw] unity 전용 클래스는 c#의 축약식(??=)을 사용하면 인식하지 못하므로 풀어씀.
                // TextMeshProUGUI는 unity 내부 클래스는 아니지만 오류방어를 위해 풀어씀.
                if (_core == null)
                {
                    _core = GetComponent<TextMeshProUGUI>();
                    CheckLegacy();
                }
                return _core;
            }
        }
        public Color OriginalColor { get; private set; }

        public Color Color
        {
            get { return Core.color; }
            set { Core.color = value; }
        }
        public string Text
        {
            get { return Core.text; }
            set { Core.text = value; }
        }        
        public override bool TouchCollider
        {
            get { return Core.raycastTarget; }
            set { Core.raycastTarget = value; }
        }

        //================================================================================
        // MonoBehavior 메서드 모음
        //================================================================================                  
        protected override void OnAwake()
        {
            base.OnAwake();

            _core = GetComponent<TextMeshProUGUI>();            
            OriginalColor = Color;

            CheckLegacy();
        }

        private void CheckLegacy()
        {
            if (_core == null)
            {
                var textLegacy = GetComponent<Text>();
                if (textLegacy != null)
                {
                    Debug.LogError($"구버전 UIText가 사용되고 있습니다. name:{name}, parent:{transform.parent.name}");
                }
            }
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        public void SetText(int value, Color? newColor = null)
        {
            SetText(value.ToString(), newColor);
        }
        public void SetText(long value, Color? newColor = null)
        {
            SetText(value.ToString(), newColor);
        }
        public void SetText(float value, Color? newColor = null)
        {
            SetText(value.ToString(), newColor);
        }
        public void SetText(double value, Color? newColor = null)
        {
            SetText(value.ToString(), newColor);
        }
        public void SetText(string text, Color? newColor = null)
        {            
            Text = text;
            if (newColor != null)
            {
                SetColor(newColor.Value);
            }
        }

        public void SetEmpty()
        {            
            Text = string.Empty;
        }

        public void SetColor(Color newColor)
        {
            Color = newColor;
        }

        public void ResetColor()
        {
            Color = OriginalColor;
        }
    }
}
