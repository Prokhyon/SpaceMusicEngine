using Gamekit3D;
using Gamekit3D.Message;
using UnityEngine;

namespace Gamekit3D.Experiment
{
    // Invisible wall that blocks only the player (collision with every other layer is disabled at
    // runtime, so enemies/projectiles/props pass through freely), and removes itself once the
    // referenced boss dies — used to trap the player in the boss arena until it's defeated.
    [RequireComponent(typeof(BoxCollider))]
    public class PlayerOnlyBarrier : MonoBehaviour, IMessageReceiver
    {
        public Damageable bossDamageable;

        void Awake()
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            for (int layer = 0; layer < 32; layer++)
            {
                if (layer == playerLayer) continue;
                Physics.IgnoreLayerCollision(gameObject.layer, layer, true);
            }
        }

        void Start()
        {
            if (bossDamageable != null && !bossDamageable.onDamageMessageReceivers.Contains(this))
            {
                bossDamageable.onDamageMessageReceivers.Add(this);
            }
        }

        public void OnReceiveMessage(MessageType type, object sender, object msg)
        {
            if (type == MessageType.DEAD)
            {
                gameObject.SetActive(false);
            }
        }
    }
}
