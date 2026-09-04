using System;
using System.Collections.Generic;

namespace Gamekit3D.Experiment
{
    public enum ExperimentGroup
    {
        A,
        B,
    }

    public enum MusicCondition
    {
        Adaptive,
        Static,
    }

    [Serializable]
    public class QuestionAnswer
    {
        public string id;
        public string question;
        public string answer;

        public QuestionAnswer(string id, string question, string answer)
        {
            this.id = id;
            this.question = question;
            this.answer = answer;
        }
    }

    [Serializable]
    public class StroopTrial
    {
        public int index;
        public string word;
        public string inkColor;
        public string clickedColor;
        public bool correct;
        public float rt_ms;
        public bool isPractice;
    }

    [Serializable]
    public class StroopResult
    {
        public List<StroopTrial> trials = new List<StroopTrial>();
        public float meanRtMs;
        public int errorCount;
        public float errorRate;
    }

    [Serializable]
    public class CheckpointEvent
    {
        public string checkpoint_id;
        public float session_time_s;
        public float player_health_at_checkpoint;
    }

    [Serializable]
    public class LevelCompletedEvent
    {
        public float session_time_s;
        public string completion_status;
    }

    [Serializable]
    public class DamageEvent
    {
        public float session_time_s;
        public float damage_amount;
        public string source_enemy_id;
        public float player_health_after;
    }

    [Serializable]
    public class DeathEvent
    {
        public float session_time_s;
        public float pos_x;
        public float pos_y;
        public float pos_z;
        public int death_count_total;
    }

    [Serializable]
    public class RespawnEvent
    {
        public float session_time_s;
        public string respawn_checkpoint_id;
    }

    [Serializable]
    public class AttackEvent
    {
        public float session_time_s;
        public string attack_type;
        public bool hit_success;
    }

    [Serializable]
    public class EnemyEliminatedEvent
    {
        public float session_time_s;
        public string enemy_type;
        public string enemy_id;
        public float pos_x;
        public float pos_y;
        public float pos_z;
    }

    [Serializable]
    public class MovementSample
    {
        public float session_time_s;
        public float pos_x;
        public float pos_y;
        public float pos_z;
        public float velocity_magnitude;
    }

    [Serializable]
    public class MusicStateChangeEvent
    {
        public float session_time_s;
        public string previous_state;
        public string new_state;
        public string trigger_type;
    }

    [Serializable]
    public class SessionLog
    {
        public string participant_id;
        public int session_number;
        public string level_id;
        public string music_condition;
        public string group;
        public string session_start_datetime;
        public string session_end_datetime;
        public float session_duration_s;

        public List<CheckpointEvent> checkpoints = new List<CheckpointEvent>();
        public LevelCompletedEvent level_completed;
        public List<DamageEvent> damage_events = new List<DamageEvent>();
        public List<DeathEvent> deaths = new List<DeathEvent>();
        public List<RespawnEvent> respawns = new List<RespawnEvent>();
        public List<AttackEvent> attacks = new List<AttackEvent>();
        public List<EnemyEliminatedEvent> enemies_eliminated = new List<EnemyEliminatedEvent>();
        public List<MovementSample> movement_samples = new List<MovementSample>();
        public List<MusicStateChangeEvent> music_state_changes = new List<MusicStateChangeEvent>();
    }

    [Serializable]
    public class SessionBlock
    {
        public string level_id;
        public string music_condition;
        public string session_start_datetime;
        public string session_end_datetime;
        public StroopResult stroop_post;
        public float time_estimate_minutes;
        public List<QuestionAnswer> emotion = new List<QuestionAnswer>();
        public List<QuestionAnswer> ieq = new List<QuestionAnswer>();
        public SessionLog gameplay_log;
    }

    [Serializable]
    public class ExperimentSession
    {
        public string participant_id;
        public string group;
        public string export_datetime;
        public List<QuestionAnswer> pre_questionnaire = new List<QuestionAnswer>();
        public StroopResult stroop_baseline;
        public SessionBlock session_1 = new SessionBlock();
        public SessionBlock session_2 = new SessionBlock();
        public List<QuestionAnswer> final_questionnaire = new List<QuestionAnswer>();
    }
}
