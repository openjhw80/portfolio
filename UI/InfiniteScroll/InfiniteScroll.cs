using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


namespace Seeder
{
    [RequireComponent(typeof(ScrollRect))]
    public partial class InfiniteScroll : MonoBehaviour
    {
        [SerializeField] private GameObject _itemPrefab;
        [SerializeField] private RectOffset _padding;
        [SerializeField] private Vector2 _cellSize;
        [SerializeField] private Vector2 _spacing;
        [SerializeField][Min(1)] private int _countPerLine = 1;

        //====== 외부 참조 개체 ======//        
        private Rect _scrollArea;        
        private RectTransform _content;                

        //====== 내부 사용 변수 ======//
        // 0이면 vertiacal, 그 이외의 값은 horizontal.
        private bool _isVertical;
        private int _lineUsed;  // 스크롤 view영역에 따라 재사용으로 활용하기 위해 필요한 line 수.
        private int _lineMax;   // 전체 contents가 표시되기 위해 필요한 최대 line 수.
        private int _lineHead;  // 스크롤 영역에 첫번째로 표시되고 있는 라인의 인덱스.        
        private int _lineHeadNext;  // 스크롤 업데이트에 따른 다음 line head 인덱스.
        private bool _dirty;    // 리스트 변경 사항에 따라 아이템 업데이트가 필요한 경우 true.        

        // RectTransform이 필수적으로 사용되는 component이므로 list item을 이것으로 저장한다.
        private List<RectTransform> _objItems;
        private Vector2 _cellSizeScaled;
        private Vector2 _spacingScaled;

        public ScrollRect Scroll { get; private set; }
        public int ItemCount { get; private set; }  // 전체 아이템 데이터 수.
        private Vector2 ItemArea { get { return _cellSize + _spacing; } }
        private Vector2 ItemAreaScaled { get { return _cellSizeScaled + _spacingScaled; } }

        // rx stream, 이벤트 관련 변수들        
        private Action<GameObject> _cbReadyItem;    // 아이템 데이터 사용전 준비를 위한 콜백.
        private Action<int, GameObject> _cbUpdateItem;    // 아이템 데이터를 채우기 위한 콜백.

        //================================================================================
        // MonoBehavior 메서드 모음
        //================================================================================
        void Awake()
        {            
            Scroll = GetComponent<ScrollRect>();
            Scroll.onValueChanged.AddListener(OnChangeScroll);
            _isVertical = Scroll.vertical || !Scroll.horizontal;
            _scrollArea = GetComponent<RectTransform>().rect;            
            _content = Scroll.content;
            _content.pivot = Vector2.up;
            _content.anchorMin = Vector2.up;
            _content.anchorMax = Vector2.up;
            _cellSizeScaled = _cellSize * _content.localScale;
            _spacingScaled = _spacing * _content.localScale;

            CreateItem();
        }

        // 아이템 업데이트 진행.
        void Update()
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
            // head가 0보다 작으면 모든 리스트 업데이트
            if (_lineHead < 0)
            {
                // position 계산을하면서 필요한 item을 활성화.
                for (int i = 0; i < _lineUsed; i++)
                {
                    SetPosition(i);
                }

                Scroll.StopMovement();
                _content.anchoredPosition = Vector2.zero;
                _lineHead = 0;
            }
            // line head가 변경된 경우, 라인의 위치 및 리스트 아이템 업데이트.
            else if (_lineHead != _lineHeadNext)
            {
                // next값이 크면 정방향, 작으면 역방향이다.
                var isForward = _lineHead < _lineHeadNext;
                if (isForward)
                {
                    // 차이값 만큼 각 line을 업데이트.
                    var count = _lineHeadNext - _lineHead;
                    for (var i = 0; i < count; i++)
                    {
                        SetPosition(_lineHead + _lineUsed + i);
                    }
                }
                else
                {
                    // 차이값 만큼 각 line을 업데이트.
                    var count = _lineHead - _lineHeadNext;
                    for (var i = 0; i < count; i++)
                    {
                        SetPosition(_lineHead - 1 - i);
                    }
                }

                _lineHead = _lineHeadNext;
            }
        }

        //====== Scroll값 변경 이벤트에 대한 처리 ======//        
        private void OnChangeScroll(Vector2 normalizedPos)
        {
            var normalizedValue = _isVertical ? normalizedPos.y : normalizedPos.x;
            // 스크롤 영역을 벗어나면 item 설정처리를 하지 않는다.
            if (normalizedValue <= 0 || normalizedValue >= 1f)
            {
                return;
            }

            if (_isVertical)
            {
                var yPos = _content.anchoredPosition.y;
                _lineHeadNext = (int)((yPos - _padding.top) / ItemArea.y);
            }
            else
            {
                // 가로 스크롤은 좌측으로 움직이므로 x이동값이 음수가 된다. 따라서 마이너스를 붙여서 계산해야 한다.                
                var xPos = -_content.anchoredPosition.x;
                _lineHeadNext = (int)((xPos - _padding.left) / ItemAreaScaled.x);
            }
            _dirty = true;
        }

        //================================================================================
        // 기초 메서드(생성자, 초기화, destroy, dispose등) 모음
        //================================================================================  
        //====== 리스트 아이템 생성 ======//        
        private void CreateItem()
        {
            // 재사용라인 수 설정. 영역에 완전히 포함되는 라인수 + 영역에 걸치는 라인 1 + 추가 대기라인 1.            
            if (_isVertical)
            {
                //var itemAreaScaled = ItemArea * _content.localScale;
                _lineUsed = (int)(_scrollArea.height / ItemAreaScaled.y) + 2;
            }
            else
            {
                //var itemAreaScaled = ItemArea * _content.localScale;
                _lineUsed = (int)(_scrollArea.width / ItemAreaScaled.x) + 2;
            }

            // 필요한 아이템 개수 계산. 라인수 * 라인당 아이템 개수.
            var usedCount = _lineUsed * _countPerLine;
            // 리스트 item 생성.            
            if (_objItems == null)
            {
                _objItems = new List<RectTransform>();
            }    
            else
            {
                _objItems.Clear();
            }            
            // 이미 생성된 것을 먼저 추가.            
            for (var i = 0; i < _content.childCount; i++)
            {
                var itemTf = _content.GetChild(i);
                if (_objItems.Count < usedCount)
                {
                    AddItem(itemTf.gameObject);
                }
                else
                {
                    itemTf.gameObject.SetActive(false);
                }   
            }
            // 모자란 개수 만큼 추가.
            var remainCount = usedCount - _content.childCount;            
            for (int i = 0; i < remainCount; i++)
            {
                // 일반 transform으로 위치 및 크기 설정.
                var itemGo = Instantiate(_itemPrefab, Vector3.zero, Quaternion.identity, _content);
                AddItem(itemGo);
            }
        }

        private void AddItem(GameObject itemGo)
        {
            itemGo.SetActive(false);
            var itemTf = itemGo.transform;
            itemTf.localScale = Vector3.one;
            itemTf.localPosition = Vector3.zero;

            // rect로 영역 설정 및 저장.
            var itemRect = itemGo.GetComponent<RectTransform>();
            itemRect.pivot = Vector2.up;
            itemRect.anchorMin = Vector2.up;
            itemRect.anchorMax = Vector2.up;
            _objItems.Add(itemRect);
        }

        //================================================================================
        // 일반 메서드 모음 
        //================================================================================
        //====== 콜백 등록 ======//        
        public void SetCBReadyItem(Action<GameObject> cbReadyItem)
        {
            _cbReadyItem = cbReadyItem;
        }
        public void SetCBUpdateItem(Action<int, GameObject> cbUpdateItem)
        {
            _cbUpdateItem = cbUpdateItem;
        }        

        //====== 리스트 데이터 설정 ======//        
        public void SetData(int count)
        {
            ItemCount = count;
            // 모든 아이템 표시를 위한 최대 라인수 계산.
            _lineMax = count / _countPerLine;
            // 나머지가 없으면 추가 라인이 0, 나머지가 있으면 추가 라인 +1.
            _lineMax += count % _countPerLine == 0 ? 0 : 1;
            // content size 설정.
            if (_isVertical)
            {                
                var contentSize = _content.sizeDelta;
                // 전체 라인수와 아이템크기를 곱한 값에서 마지막 아이템은 spacing을 사용하지 않으므로 빼준다.
                contentSize.y = (_lineMax * ItemArea.y) - _spacing.y + _padding.top + _padding.bottom;
                _content.sizeDelta = contentSize;
                _content.anchoredPosition = Vector2.zero;
            }
            else
            {
                var contentSize = _content.sizeDelta;
                // 전체 라인수와 아이템크기를 곱한 값에서 마지막 아이템은 spacing을 사용하지 않으므로 빼준다.
                contentSize.x = (_lineMax * ItemArea.x) - _spacing.x + _padding.left + _padding.right;
                _content.sizeDelta = contentSize;
                _content.anchoredPosition = Vector2.zero;
            }

            // item 전체 비활성화.
            foreach (var item in _objItems)
            {
                item.gameObject.SetActive(false);
            }

            // 전체 라인 업데이트를 위해 헤드를 음수로 설정.
            _lineHead = -1;
            _dirty = true;
        }

        // 리스트의 line 위치 설정.
        private void SetPosition(int lineIndex)
        {
            if (_isVertical)
            {
                SetPositionVertical(lineIndex);
            }
            else
            {
                SetPositionHorizontal(lineIndex);
            }
        }
        // 세로 스크롤의 라인 index에 해당하는 각 아이템 위치 및 데이터 설정.
        private void SetPositionVertical(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lineMax)
            {
                return;
            }

            var startIndex = lineIndex * _countPerLine;
            var itemArea = ItemArea;
            // 아이템이 아래로 배치되므로 음수값으로 설정해야 한다.
            var yPos = -((itemArea.y * lineIndex) + _padding.top);
            for (var i = 0; i < _countPerLine; i++)
            {
                var itemIndex = startIndex + i;
                // item 개수는 재활용 되므로 전체 아이템 index를 전체 obj로 나누어 나머지를 index로 사용한다.
                var item = _objItems[itemIndex % _objItems.Count];
                var itemPos = item.anchoredPosition;
                itemPos.x = (itemArea.x * i) + _padding.left;
                itemPos.y = yPos;
                item.anchoredPosition = itemPos;

                // 활성화 처리.                
                var isActive = itemIndex < ItemCount;
                item.gameObject.SetActive(isActive);
                if (isActive)
                {
                    _cbUpdateItem?.Invoke(itemIndex, item.gameObject);
                }
            }
        }
        // 가로 스크롤의 라인 index에 해당하는 각 아이템 위치 및 데이터 설정.
        private void SetPositionHorizontal(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= _lineMax)
            {
                return;
            }

            var startIndex = lineIndex * _countPerLine;
            var itemArea = ItemArea;
            var xPos = (itemArea.x * lineIndex) + _padding.left;
            for (var i = 0; i < _countPerLine; i++)
            {
                var itemIndex = startIndex + i;
                // item 개수는 재활용 되므로 전체 아이템 index를 전체 obj로 나누어 나머지를 index로 사용한다.
                var item = _objItems[itemIndex % _objItems.Count];
                var itemPos = item.anchoredPosition;
                itemPos.x = xPos;
                // 아이템이 아래로 배치되므로 음수값으로 설정해야 한다.
                itemPos.y = -((itemArea.y * i) + _padding.top);
                item.anchoredPosition = itemPos;

                // 활성화 처리.                
                var isActive = itemIndex < ItemCount;
                item.gameObject.SetActive(isActive);
                if (isActive)
                {
                    _cbUpdateItem?.Invoke(itemIndex, item.gameObject);
                }
            }
        }
    }
}
