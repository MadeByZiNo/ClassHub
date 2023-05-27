﻿namespace ClassHub.Shared {
    public class Lecture {
        public int lecture_id { get; set; }
        public int room_id { get; set; }
        public int week { get; set; }
        public int chapter { get; set; }
        public string title { get; set; }
        public string contents { get; set; }
        public DateTime start_date { get; set; }
        public DateTime end_date { get; set; }
        public string video_url { get; set; }
        public int learning_time { get; set; }
    }

    public class LectureProgress {
        public int room_id { get; set; }
        public int lecture_id { get; set; }
        public int student_id { get; set; }
        public int elapsed_time { get; set; }
        public bool is_enroll { get; set; }
    }
}
