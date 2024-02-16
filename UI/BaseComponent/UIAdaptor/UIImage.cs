using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;


namespace Seeder
{
    // Image 제어를 위한 ui adaptor.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public class UIImage : UIAdaptor
    {
        private Image _core;
        // 마지막에 로드를 시도한 이미지의 애셋이름.
        private string _lastAssetName;

        // 기본 변수들.
        public Image Core
        {
            get
            {
                // [220523][jhw] unity 전용 클래스는 c#의 축약식(??=)을 사용하면 인식하지 못하므로 풀어씀.
                if (_core == null)
                {
                    _core = GetComponent<Image>();
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
        public Sprite Image
        {
            get { return Core.sprite; }
            set { Core.sprite = value; }
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

            _core = GetComponent<Image>();
            OriginalColor = Core.color;
        }

        //================================================================================
        // 디버그 및 테스트용 메서드 모음
        //================================================================================

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        public void SetEmpty()
        {
            _lastAssetName = string.Empty;
            Core.sprite = null;
        }

        public void SetImage(Sprite sprite)
        {
            _lastAssetName = string.Empty;
            Core.sprite = sprite;
        }

        public void SetImage(string spriteName)
        {
            var cachedSprite = SystemCenter.UIMgr.GetSprite(spriteName);
            if (cachedSprite != null)
            {
                SetImage(cachedSprite);
                return;
            }            

#if UNITY_EDITOR
            // 존재하지 않는 이미지면 null로 설정. 속도이슈가 있으므로 에디터에서만 실행.
            if (!ResourceLibrary.ExistAddressable(spriteName))
            {                
                SetEmpty();
                return;
            }
#endif

            _lastAssetName = spriteName;
            Addressables.LoadAssetAsync<Sprite>(spriteName)
                .Completed += (handler) =>
                {
                    var sprite = handler.Result;
                    SystemCenter.UIMgr.AddSprite(spriteName, sprite);

                    // 로드시에는 마지막에 설정한 이미지와 같을 때만 설정한다.
                    if (_lastAssetName == spriteName)
                    {                        
                        SetImage(sprite);
                    }
                };
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
