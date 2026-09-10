using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PuyoGame
{
    /// <summary>
    /// 押している間の状態が取れるボタン。
    /// UI の Button は「離したとき」しか分からないので、
    /// 押しっぱなしの連続移動やソフトドロップにはこちらを使う。
    /// </summary>
    [DisallowMultipleComponent]
    public class HoldButton : MonoBehaviour,
        IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [Tooltip("押したときに少し沈ませる量")]
        [Range(0f, 0.2f)]
        [SerializeField] float pressScale = 0.06f;

        /// <summary>いま押されているか。</summary>
        public bool IsHeld { get; private set; }

        /// <summary>押した瞬間に発火する。</summary>
        public event Action Pressed;

        Vector3 baseScale = Vector3.one;
        bool captured;

        void Awake()
        {
            Capture();
        }

        void OnDisable()
        {
            Release();
        }

        void Capture()
        {
            if (captured) return;
            baseScale = transform.localScale;
            captured = true;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            Capture();
            IsHeld = true;
            transform.localScale = baseScale * (1f - pressScale);
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) => Release();

        // 押したまま指がボタンの外へ出たときも、押しっぱなし扱いを解く
        public void OnPointerExit(PointerEventData eventData) => Release();

        void Release()
        {
            if (!IsHeld) return;
            IsHeld = false;
            if (captured) transform.localScale = baseScale;
        }
    }
}
