
﻿namespace ClassHub.Shared {
    public class JudgeRequest {
        public string CorrectCode { get; set; }           // 교수가 제출한 정답 코드
        public string SubmitCode { get; set; }            // 학생이 에디터에 작성하여 제출한 코드
        public string Language { get; set; }              // 코드의 프로그래밍 언어
        public List<string> InputCases { get; set; }      // 입력 테스트 케이스
        public double ExecutionTimeLimit { get; set; }    // 실행 시간 제한
        public long MemoryUsageLimit { get; set; }        // 메모리 사용량 제한
    }
}
