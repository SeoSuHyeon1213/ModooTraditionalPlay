using UnityEngine;

namespace FindOurSound.JangGuRhythm
{
    /// <summary>
    /// 장구 좌/우에 배치되는 트리거 콜라이더. 어떤 컨트롤러가 쳤는지 ControllerSideMarker로 판별해서
    /// 피드백을 표시하고, NoteSpawner(있다면)에 판정을 전달한다.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class JangguHitTrigger : MonoBehaviour
    {
        [Tooltip("이 트리거가 담당하는 장구 쪽 (왼쪽=북편/원, 오른쪽=채편/사각형)")]
        public NoteSide side;

        public NoteSpawner noteSpawner;
        public JangguHitFeedback feedback;

        [Tooltip("연속 타격 사이 최소 간격(초). 콜라이더가 여러 프레임 겹칠 때 중복 판정을 막는다.")]
        public float hitCooldown = 0.15f;

        float lastHitTime = -999f;

        void OnTriggerEnter(Collider other)
        {
            if (Time.time - lastHitTime < hitCooldown) return;

            ControllerSideMarker marker = other.GetComponentInParent<ControllerSideMarker>();
            if (marker == null || !marker.AcceptsCollider(other)) return;

            lastHitTime = Time.time;

            bool correctHand = marker.side == side;
            feedback?.ShowHit(side, marker.side, correctHand);

            Debug.Log($"[JangGuRhythm] {name} 트리거 충돌: other={other.name}, controllerSide={marker.side}, correctHand={correctHand}");

            if (correctHand)
            {
                noteSpawner?.TryHit(side);
            }
        }
    }
}
