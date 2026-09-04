using UnityEngine;

namespace Gamekit3D.Experiment
{
    public abstract class ExperimentPanelBase : MonoBehaviour
    {
        public virtual void Show()
        {
            gameObject.SetActive(true);
        }

        public virtual void Hide()
        {
            gameObject.SetActive(false);
        }
    }
}
