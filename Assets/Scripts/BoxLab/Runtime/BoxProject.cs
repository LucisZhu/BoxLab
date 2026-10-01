using System.Collections.Generic;
using UnityEngine;

namespace BoxLab
{
    /// <summary>The authoring asset. Gameplay always operates on a cloned level and rule book.</summary>
    [CreateAssetMenu(fileName = "BoxProject", menuName = "BoxLab/项目")]
    public sealed class BoxProject : ScriptableObject
    {
        public RuleBookData book = new RuleBookData();
        public List<LevelData> levels = new List<LevelData>();
        public int selectedLevel;

        public LevelData SelectedLevel
        {
            get
            {
                if (levels == null || levels.Count == 0) return null;
                return levels[Mathf.Clamp(selectedLevel, 0, levels.Count - 1)];
            }
        }
    }
}
