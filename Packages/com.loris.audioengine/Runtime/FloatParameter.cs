using UnityEngine;

namespace Loris.AudioEngine
{
    [CreateAssetMenu(fileName = "FloatParameter", menuName = "SpaceMusicEngine/Parameters/Float Parameter")]
    public class FloatParameter : ParameterDefinition
    {
        [SerializeField] private float value;

        public float Value
        {
            get => value;
            set => this.value = value;
        }

        public override float RawValue => value;
    }
}
