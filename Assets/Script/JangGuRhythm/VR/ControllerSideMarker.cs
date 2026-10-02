using UnityEngine;

namespace FindOurSound.JangGuRhythm
{
    /// <summary>
    /// XR 컨트롤러(Left Controller / Right Controller) 오브젝트에 붙여서
    /// "이 콜라이더 계층은 왼쪽/오른쪽 컨트롤러 소속"임을 표시한다.
    /// JangguHitTrigger가 OnTriggerEnter 시 GetComponentInParent로 이 마커를 찾아 어느 손인지 판별한다.
    /// </summary>
    public class ControllerSideMarker : MonoBehaviour
    {
        public NoteSide side;

        [Tooltip("외형과 분리한 타격 전용 Collider만 연결한다. 비어 있으면 기존 자식 Collider 판정을 유지한다.")]
        [SerializeField] private Collider[] hitColliders = new Collider[0];

        public bool AcceptsCollider(Collider candidate)
        {
            if (!isActiveAndEnabled || candidate == null) return false;
            if (hitColliders == null || hitColliders.Length == 0) return true;
            foreach (Collider hitCollider in hitColliders)
            {
                if (hitCollider == candidate) return true;
            }
            return false;
        }
    }
}
