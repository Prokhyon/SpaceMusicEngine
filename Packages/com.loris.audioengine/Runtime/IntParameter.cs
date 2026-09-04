using UnityEngine;

namespace Loris.AudioEngine
{
    [CreateAssetMenu(fileName = "IntParameter", menuName = "SpaceMusicEngine/Parameters/Int Parameter")]
    public class IntParameter : ParameterDefinition
    {
        [SerializeField] private int value;

        public int Value
        {
            get => value;
            set => this.value = value;
        }

        public override float RawValue => value;
    }
}
