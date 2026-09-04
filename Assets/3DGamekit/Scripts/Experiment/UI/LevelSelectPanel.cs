using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gamekit3D.Experiment
{
    // Quick-access panel on the Start screen: lets the experimenter jump straight into any of the
    // 4 level / music-condition combinations, bypassing the full questionnaire protocol.
    public class LevelSelectPanel : MonoBehaviour
    {
        public void LoadLevel1Adaptive() => SceneManager.LoadScene("Level1");
        public void LoadLevel1Static() => SceneManager.LoadScene("Level1_Static");
        public void LoadLevel2Adaptive() => SceneManager.LoadScene("Level2");
        public void LoadLevel2Static() => SceneManager.LoadScene("Level2_Static");
    }
}
