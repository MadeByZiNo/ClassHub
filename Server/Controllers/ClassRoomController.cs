﻿using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using ClassHub.Shared;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ClassHub.Server.Controllers {
    [Route("api/[controller]")]
    [ApiController]
    public class ClassRoomController : ControllerBase {
        const string host = "classdb.postgres.database.azure.com";
        const string username = "byungmeo";
        const string passwd = "Mju12345!#";
        const string database = "classdb";
        const string connectionString = $"Host={host};Username={username};Password={passwd};Database={database}";

        const string blobStorageUri = "https://classhubfilestorage.blob.core.windows.net/";
        const string vaultStorageUri = "https://azureblobsecret.vault.azure.net/";
        const string academicServerUri = "https://academicinfo.azurewebsites.net/";

		private readonly ILogger<ClassRoomController> _logger;

		public ClassRoomController(ILogger<ClassRoomController> logger) {
			_logger = logger;
		}

		// Param으로 받은 ID를 가진 강의실의 정보를 불러옴
		// 실제 요청 url 예시 : 'api/classroom/1'
		[HttpGet("{room_id}")]
        public ClassRoom GetClassRoom(int room_id) {
            using var connection = new NpgsqlConnection(connectionString);
            var query = 
                "SELECT * " +
                "FROM classroom " +
                "WHERE room_id = @room_id;";
            var parameters = new DynamicParameters();
            parameters.Add("room_id", room_id);
            var result = connection.QuerySingle<ClassRoom>(query, parameters); // ID는 고유하므로, 하나만 반환되는 것이 자명하여 QuerySingle 사용
            return result;
        }

        public async Task<List<ClassRoomDetail>> GetClassRoomDetailList(List<ClassRoom> classRoomList, string accessToken) {
            // 학사정보DB로부터 시간표 정보를 가져온다
            List<ClassRoomDetail> classRoomDetailList = new List<ClassRoomDetail>();
            foreach(var classRoom in classRoomList) {
                using var academicClient = new HttpClient {
                    BaseAddress = new Uri(academicServerUri)
                };
                try {
                    var classRoomDetail = await academicClient.GetFromJsonAsync<ClassRoomDetail>(
                        $"ClassRoomDetail?" +
                        $"course_id={classRoom.course_id}" +
                        $"&section_id={classRoom.section_id}" +
                        $"&semester={classRoom.semester}" +
                        $"&year={classRoom.year}" +
                        $"&accessToken={accessToken}"
                    );
                    classRoomDetail.room_id = classRoom.room_id;
                    classRoomDetail.course_id = classRoom.course_id;
                    classRoomDetail.section_id = classRoom.section_id;
                    classRoomDetail.semester = classRoom.semester;
                    classRoomDetail.year = classRoom.year;
                    classRoomDetail.title = classRoom.title;
                    classRoomDetailList.Add(classRoomDetail);
                } catch(Exception ex) {
                    _logger.LogError($"학사정보DB에서 강의실 세부정보를 불러오는데 실패");
                    _logger.LogError(ex.Message);
                }
            }
            return classRoomDetailList;
        }

        public async Task<ClassRoom> GetClassRoomBySectionId(string course_id, int section_id, string semester, int year) {
            using var connection = new NpgsqlConnection(connectionString);
            var query =
                "SELECT * " +
                "FROM classroom " +
                "WHERE course_id = @course_id AND section_id = @section_id AND semester = @semester AND year = @year;";
            var parameters = new DynamicParameters();
            parameters.Add("course_id", course_id);
            parameters.Add("section_id", section_id);
            parameters.Add("semester", semester);
            parameters.Add("year", year);
            var classRoom = connection.QuerySingle<ClassRoom>(query, parameters);
            return classRoom;
        }

        // 학생이 수강 중인 강의의 강의실 리스트를 불러옴
        // 실제 요청 url 예시 : 'api/classroom/takes'
        [HttpGet("takes")]
        public async Task<IActionResult> GetTakesClassRoomList([FromQuery] int student_id, [FromQuery] string accessToken) {
            _logger.LogInformation($"GetTakesClassRoomList?student_id={student_id}");
            // TODO: 년도, 학기별 구분이 필요함
            // 학생이 수강 중인 모든 강의실의 room_id를 불러온다
            using var connection = new NpgsqlConnection(connectionString);
            var query =
                "SELECT room_id " +
                "FROM student " +
                "WHERE student_id = @student_id;";
            var parameters = new DynamicParameters();
            parameters.Add("student_id", student_id);
            var roomIdList = connection.Query<int>(query, parameters);

            // 각 room_id에 대해 강의실 정보를 불러온다
            List<ClassRoom> classRoomList = new List<ClassRoom>();
            foreach(var roomId in roomIdList) {
                query =
                    "SELECT * " +
                    "FROM classroom " +
                    "WHERE room_id = @room_id;";
                parameters = new DynamicParameters();
                parameters.Add("room_id", roomId);
                var classRoom = connection.QuerySingle<ClassRoom>(query, parameters);
                classRoomList.Add(classRoom);
            }

            var classRoomDetailList = await GetClassRoomDetailList(classRoomList, accessToken);
            return Ok(classRoomDetailList);
        }

		// 교수가 강의 중인 강의실 리스트를 불러옴
		// 실제 요청 url 예시 : 'api/classroom/teaches'
		[HttpGet("teaches")]
        public async Task<IActionResult> GetTeachesClassRoomList([FromQuery] int instructor_id, [FromQuery] string accessToken) {
            _logger.LogInformation($"GetTeachesClassRoomList?instructor_id={instructor_id}");
            string requestUri = $"teaches/all?id={instructor_id}&accessToken={accessToken}";
            List<ClassRoom>? classRoomList = new List<ClassRoom>();
            using(var academicClient = new HttpClient { BaseAddress = new Uri(academicServerUri) }) {
                classRoomList = await academicClient.GetFromJsonAsync<List<ClassRoom>>(requestUri);
                if(classRoomList == null) classRoomList = new List<ClassRoom>();
                for(int i = 0; i < classRoomList.Count; i++) {
                    var classRoom = classRoomList[i];
                    switch(int.Parse(classRoom.semester)) {
                        case 1:
                            classRoom.semester = "Spring";
                            break;
                        case 2:
                            classRoom.semester = "Summer";
                            break;
                        case 3:
                            classRoom.semester = "Fall";
                            break;
                        case 4:
                            classRoom.semester = "Winter";
                            break;
                        default:
                            break;
                    }
                    classRoomList[i] = await GetClassRoomBySectionId(classRoom.course_id, classRoom.section_id, classRoom.semester, classRoom.year);
                }
            }

            var classRoomDetailList = await GetClassRoomDetailList(classRoomList, accessToken);
            return Ok(classRoomDetailList);
		}

		// Param으로 받은 ID를 가진 강의실의 모든 강의자료를 불러옴
		// 실제 요청 url 예시 : 'api/classroom/1/lecturematerial/all'
		[HttpGet("{room_id}/lecturematerial/all")]
        public IEnumerable<LectureMaterial> GetLectureMaterialListInClassRoom(int room_id) {
            using var connection = new NpgsqlConnection(connectionString);
            string query = 
                "SELECT * " +
                "FROM lecturematerial " +
                "WHERE room_id = @room_id;";
            var parameters = new DynamicParameters();
            parameters.Add("room_id", room_id);
            var result = connection.Query<LectureMaterial>(query, parameters);
            return result;
        }

        // Param으로 받은 ID를 가진 강의실의 모든 공지사항을 불러옴
        // 실제 요청 url 예시 : 'api/classroom/1/notice/all'
        [HttpGet("{room_id}/notice/all")]
        public IEnumerable<Notice> GetNoticeListInClassRoom(int room_id) {
            using var connection = new NpgsqlConnection(connectionString);
            string query =
                "SELECT * " +
                "FROM notice " +
                "WHERE room_id = @room_id;";
            var parameters = new DynamicParameters();
            parameters.Add("room_id", room_id);
            var result = connection.Query<Notice>(query, parameters);
            return result;
        }

        // Param으로 받은 학번을 가진 학생에게 온 모든 알림을 불러옴 (모든 수강 강의)
        // 실제 요청 url 예시 : 'api/classroom/notification/all/60182147'
        [HttpGet("notification/all")]
        public async Task<IActionResult> GetStudentNotificationsAsync([FromQuery] int student_id, [FromQuery] string accessToken) {
            if (!await AuthService.isValidToken(student_id, accessToken)) {
                return Unauthorized("Invalid token");
            }

            _logger.LogInformation($"GetStudentNotifications?student_id={student_id}");
            using var connection = new NpgsqlConnection(connectionString);
            var query =
                "SELECT * " +
                "FROM studentnotification " +
                "WHERE student_id = @student_id;";
            var parameters = new DynamicParameters();
            parameters.Add("student_id", student_id);
            var studentNotifications = connection.Query<StudentNotification>(query, parameters);

            List<DisplayStudentNotification> result = new List<DisplayStudentNotification>();

            foreach(var item in studentNotifications) {
                query =
                    "SELECT * " +
                    "FROM classroomnotification " +
                    "WHERE notification_id = @notification_id;";
                parameters = new DynamicParameters();
                parameters.Add("notification_id", item.notification_id);
                var roomNotification = connection.QuerySingle<ClassRoomNotification>(query, parameters);
                result.Add(new DisplayStudentNotification {
                    room_id = item.room_id,
                    student_id = item.student_id,
                    notification_id = item.notification_id,
                    message = roomNotification.message,
                    uri = roomNotification.uri,
                    notify_date = roomNotification.notify_date,
                    is_read = item.is_read
                });
            }

            // DB 중복 참조를 막기 위해 강의실번호 순으로 나열 뒤 이미 구한 Title을 재사용
            result.Sort((a, b) => a.room_id.CompareTo(b.room_id));
            for(int i = 0; i < result.Count; i++) {
                if(i != 0 && result[i - 1].room_id == result[i].room_id) result[i].title = result[i - 1].title;
                else {
                    query =
                        "SELECT title " +
                        "FROM classroom " +
                        "WHERE room_id = @room_id;";
                    parameters = new DynamicParameters();
                    parameters.Add("room_id", result[i].room_id);
                    result[i].title = connection.QuerySingle<string>(query, parameters);
                }
            }
            return Ok(result);
        }

        // 수정 된 LectureMaterial 객체를 DB에 UPDATE 합니다.
        // 실제 요청 url 예시 : 'api/classroom/modify/lecturematerial'
        [HttpPut("modify/lecturematerial")]
        public void PutLectureMaterial([FromBody] LectureMaterial lectureMaterial) {
            using var connection = new NpgsqlConnection(connectionString);
            string query = 
                "UPDATE lecturematerial " +
                "SET (week, title, contents, up_date) = (@week, @title, @contents, @up_date) " +
                "WHERE room_id = @room_id AND material_id = @material_id;";
            connection.Execute(query, lectureMaterial);

            InsertNotificationWithStudent(new ClassRoomNotification {
                room_id = lectureMaterial.room_id,
                message = $"(수정) {lectureMaterial.title}",
                uri = $"classroom/{lectureMaterial.room_id}/notice/{lectureMaterial.material_id}",
                notify_date = DateTime.Now
            });
        }

        // LectureMaterial 객체를 DB에 INSERT 합니다. material_id 값이 함께 반환됩니다.
        // 실제 요청 url 예시 : 'api/classroom/register/lecturematerial'
        [HttpPost("register/lecturematerial")]
        public ActionResult PostLectureMaterial([FromBody] LectureMaterial lectureMaterial) {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();

            // material_id 시퀀스를 가져오던 중 다른 사용자가 INSERT 작업을 수행하면 곤란하기 때문에 하나의 트랜잭션으로 묶음
            using(var transaction = connection.BeginTransaction()) {
                try {
                    string query1 = // 다음 material_id 시퀀스를 가져옴.
                        "SELECT nextval('lecturematerial_material_id_seq');";
                    lectureMaterial.material_id = connection.QuerySingle<int>(sql: query1, transaction: transaction);

                    _logger.LogInformation($"PostLectureMaterial?room_id={lectureMaterial.room_id}&material_id={lectureMaterial.material_id}");

                    string query2 =
                        "INSERT INTO lecturematerial (room_id, material_id, week, title, author, contents, publish_date, up_date, view_count) " +
                        "VALUES (@room_id, @material_id, @week, @title, @author, @contents, @publish_date, @up_date, @view_count);";
                    connection.Execute(query2, lectureMaterial);

                    transaction.Commit();
                } catch(Exception ex) {
                    _logger.LogError("강의자료 게시 중 문제가 발생하여 RollBack 합니다.");
                    _logger.LogError($"msg :\n{ex.Message}");
                    transaction.Rollback();
                    return BadRequest();
                }
            }

            InsertNotificationWithStudent(new ClassRoomNotification {
                room_id = lectureMaterial.room_id,
                message = lectureMaterial.title,
                uri = $"classroom/{lectureMaterial.room_id}/lecturematerial/{lectureMaterial.material_id}",
                notify_date = DateTime.Now
            });

            return new ObjectResult(lectureMaterial.material_id);
        }

        // 강의자료 첨부파일들을 Blob Storage에 업로드 합니다.
        // 실제 요청 url 예시 : 'api/classroom/upload/lecturematerial'
        [HttpPost("{room_id}/upload/lecturematerial/{material_id}")]
        public async Task<IActionResult> UploadLectureMaterialFiles(int room_id, int material_id, List<IFormFile> files) {
            var blobServiceClient = new BlobServiceClient(
                new Uri(blobStorageUri),
                new DefaultAzureCredential()
            );

            // 컨테이너 객체를 받음
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("lecturematerial");

            await Console.Out.WriteLineAsync($"files count: {files.Count}");
            foreach(var file in files) {
                using(var memoryStream = new MemoryStream()) {
                    await file.CopyToAsync(memoryStream);
                    memoryStream.Position = 0;

                    var blobClient = containerClient.GetBlobClient(file.FileName);
                    await Console.Out.WriteLineAsync($"Blob Name: {blobClient.Name}");
                    var response = await blobClient.UploadAsync(memoryStream, overwrite: true);
                    await Console.Out.WriteLineAsync(response.ToString());
                }
            }

            await Console.Out.WriteLineAsync("Blob Upload Success!");
            return Ok();
        }

		// 강의자료 첨부파일 목록을 불러옵니다.
		// 실제 요청 url 예시 : 'api/classroom/attachments/lecturematerial'
		[HttpGet("attachments/lecturematerial")]
        public List<Attachment> GetLectureMaterialAttachments([FromQuery] int room_id, [FromQuery] int material_id) {
            var blobServiceClient = new BlobServiceClient(
                new Uri(blobStorageUri),
                new DefaultAzureCredential()
            );

            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient("lecturematerial");
            string folderPath = $"{room_id}/{material_id}";
            List<BlobClient> blobClients = containerClient.GetBlobs(prefix: folderPath)
                .Select(blobItem => containerClient.GetBlobClient(blobItem.Name))
                .ToList();

            List<Attachment> attachments = blobClients.Select(blobClient => new Attachment {
                FileName = Path.GetFileName(blobClient.Name),
                UpDate = blobClient.GetProperties().Value.LastModified.ToString("yyyy-MM-dd HH:mm:ss"),
                FileSize = (int)(blobClient.GetProperties().Value.ContentLength / 1024) // KB 단위로 변환
            }).ToList();

            return attachments;
        }

        // BlobStorage에 저장된 Blob을 다운로드 하는 Url을 생성합니다.
        // 실제 요청 url 예시 : 'api/classroom/download'
        [HttpGet("download")]
        public string GetAttachmentDownloadUrl ([FromQuery] string container_name, [FromQuery] string blob_name) {
			var secretClient = new SecretClient(
                vaultUri: new Uri(vaultStorageUri), 
                credential: new DefaultAzureCredential()
            );
			string secretName = "StorageAccountKey";
			KeyVaultSecret secret = secretClient.GetSecret(secretName);
			var storageAccountKey = secret.Value;

			var blobServiceClient = new BlobServiceClient(
				new Uri(blobStorageUri),
				new DefaultAzureCredential()
			);

			BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(container_name);
			BlobClient blobClient = containerClient.GetBlobClient(blob_name);

			BlobSasBuilder sasBuilder = new BlobSasBuilder() {
				BlobContainerName = containerClient.Name,
				BlobName = blobClient.Name,
				Resource = "b",
				StartsOn = DateTime.UtcNow,
				ExpiresOn = DateTime.UtcNow.AddHours(1)
			};
			sasBuilder.SetPermissions(BlobSasPermissions.Read);
			string sasToken = sasBuilder.ToSasQueryParameters(new StorageSharedKeyCredential(containerClient.AccountName, storageAccountKey)).ToString();

			UriBuilder sasUri = new UriBuilder(blobClient.Uri) {
				Query = sasToken
			};
			string downloadUrl = sasUri.ToString();

			return downloadUrl;
		}

		// 수정 된 Notice 객체를 DB에 UPDATE 합니다.
		// 실제 요청 url 예시 : 'api/classroom/modify/notice'
		[HttpPut("modify/notice")]
        public void PutNotice([FromBody] Notice notice) {
            using(var connection = new NpgsqlConnection(connectionString)) {
                string query =
                "UPDATE notice " +
                "SET (title, contents, up_date) = (@title, @contents, @up_date) " +
                "WHERE room_id = @room_id AND notice_id = @notice_id;";
                connection.Execute(query, notice);
            }

            InsertNotificationWithStudent(new ClassRoomNotification {
                room_id = notice.room_id,
                message = $"(수정) {notice.title}",
                uri = $"classroom/{notice.room_id}/notice/{notice.notice_id}",
                notify_date = DateTime.Now
            });
        }

        // Board 조회수를 1 증가시켜 DB에 UPDATE 합니다.
        // 실제 요청 url 예시 : 'api/classroom/1/view/notice/1'
        [HttpPut("{room_id}/view/{kind}/{content_id}")]
        public void IncreaseViewCount(int room_id, string kind, int content_id) {
            if(kind != "notice" && kind != "material") {
                _logger.LogError("예상치 못한 kind 값");
                return;
            }
			using var connection = new NpgsqlConnection(connectionString);
            string query =
				$"UPDATE {((kind == "material") ? "lecturematerial" : kind)} " +
				$"SET view_count = view_count + 1 " +
				$"WHERE room_id = @room_id AND {kind}_id = @content_id;";
			var parameters = new DynamicParameters();
            parameters.Add("room_id", room_id);
            parameters.Add("content_id", content_id);
            connection.Execute(query, parameters);
        }

        // Notice 객체를 DB에 INSERT 합니다.
        // 실제 요청 url 예시 : 'api/classroom/register/notice'
        [HttpPost("register/notice")]
        public void PostNotice([FromBody] Notice notice) {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();

            // notice_id 시퀀스를 가져오던 중 다른 사용자가 INSERT 작업을 수행하면 곤란하기 때문에 하나의 트랜잭션으로 묶음
            using(var transaction = connection.BeginTransaction()) {
                try {
                    string query1 = // 다음 notice_id 시퀀스를 가져옴. (반환값은 최초일 경우 1이 나오지만, 쿼리가 실행된 직후 실제 DB내 시퀀스는 2로 바뀜)
                    "SELECT nextval('notice_notice_id_seq');";
                    notice.notice_id = connection.QuerySingle<int>(sql: query1, transaction: transaction);

                    _logger.LogInformation($"PostNotice?room_id={notice.room_id}&notice_id={notice.notice_id}");

                    string query2 =
                        "INSERT INTO notice (room_id, notice_id, title, author, contents, publish_date, up_date, view_count) " +
                        "VALUES (@room_id, @notice_id, @title, @author, @contents, @publish_date, @up_date, @view_count);";
                    connection.Execute(query2, notice);

                    transaction.Commit();
                } catch (Exception ex) {
                    _logger.LogError("공지사항 게시 중 문제가 발생하여 RollBack 합니다.");
                    _logger.LogError($"msg :\n{ex.Message}");
                    transaction.Rollback();
                    return;
                }
            }

            InsertNotificationWithStudent(new ClassRoomNotification {
                room_id = notice.room_id,
                message = notice.title,
                uri = $"classroom/{notice.room_id}/notice/{notice.notice_id}",
                notify_date = DateTime.Now
            });
        }

        // 공지사항을 삭제합니다
        // 실제 요청 url 예시 : 'api/classroom/1/delete/notice/1'
        [HttpDelete("{room_id}/delete/notice/{notice_id}")]
		public void DeleteNotice(int room_id, int notice_id) {
			using var connection = new NpgsqlConnection(connectionString);
			string query =
				"DELETE FROM notice " +
				"WHERE room_id = @room_id AND notice_id = @notice_id;";
			var parameters = new DynamicParameters();
			parameters.Add("room_id", room_id);
			parameters.Add("notice_id", notice_id);
			connection.Execute(query, parameters);
		}

		// 강의자료를 삭제합니다
		// 실제 요청 url 예시 : 'api/classroom/1/delete/lecturematerial/1'
		[HttpDelete("{room_id}/delete/lecturematerial/{material_id}")]
		public void DeleteLectureMaterial(int room_id, int material_id) {
			using var connection = new NpgsqlConnection(connectionString);
			string query =
				"DELETE FROM lecturematerial " +
				"WHERE room_id = @room_id AND material_id = @material_id;";
			var parameters = new DynamicParameters();
			parameters.Add("room_id", room_id);
			parameters.Add("material_id", material_id);
			connection.Execute(query, parameters);
		}

        // Param으로 받은 학번을 가진 학생에게 온 모든 강의실 알림을 불러옴 (모든 수강 강의)
        // 실제 요청 url 예시 : 'api/classroom/notification/all/60182147'
        [HttpGet("notification/all/{student_id}")]
        public IEnumerable<DisplayStudentNotification> GetStudentNotifications(int student_id) {
            _logger.LogInformation($"GetStudentNotifications?student_id={student_id}");
            using var connection = new NpgsqlConnection(connectionString);
            var query =
                "SELECT * " +
                "FROM studentnotification " +
                "WHERE student_id = @student_id;";
            var parameters = new DynamicParameters();
            parameters.Add("student_id", student_id);
            var studentNotifications = connection.Query<StudentNotification>(query, parameters);

            List<DisplayStudentNotification> result = new List<DisplayStudentNotification>();

            foreach(var item in studentNotifications) {
                query =
                    "SELECT * " +
                    "FROM classroomnotification " +
                    "WHERE notification_id = @notification_id;";
                parameters = new DynamicParameters();
                parameters.Add("notification_id", item.notification_id);
                var roomNotification = connection.QuerySingle<ClassRoomNotification>(query, parameters);
                result.Add(new DisplayStudentNotification {
                    room_id = item.room_id,
                    student_id = item.student_id,
                    notification_id = item.notification_id,
                    message = roomNotification.message,
                    uri = roomNotification.uri,
                    notify_date = roomNotification.notify_date,
                    is_read = item.is_read
                });
            }

            // DB 중복 참조를 막기 위해 강의실번호 순으로 나열 뒤 이미 구한 Title을 재사용
            result.Sort((a, b) => a.room_id.CompareTo(b.room_id));
            for(int i = 0; i < result.Count; i++) {
                if(i != 0 && result[i - 1].room_id == result[i].room_id) result[i].title = result[i - 1].title;
                else {
                    query =
                        "SELECT title " +
                        "FROM classroom " +
                        "WHERE room_id = @room_id;";
                    parameters = new DynamicParameters();
                    parameters.Add("room_id", result[i].room_id);
                    result[i].title = connection.QuerySingle<string>(query, parameters);
                }
            }
            return result;
        }

        // 강의실 테이블에 알림을 등록합니다.
        // 아직은 클라이언트에서 직접적으로 알림 INSERT를 요청하는 작업이 없기에 API를 생성하지 않습니다.
        public void InsertNotificationWithStudent(ClassRoomNotification roomNotification) {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open(); // 트랜잭션을 사용하는 경우 직접 Open() 하는 것을 권장

            // 동시성 문제 때문에 여러 개의 쿼리를 마치 하나의 작업처럼 실행시키도록 하기 위해 트랜잭션 단위로 묶습니다.
            using(var transaction = connection.BeginTransaction()) {
                try {
                    string query1 = // 다음 notification_id 시퀀스를 가져옴. (반환값은 최초일 경우 1이 나오지만, 쿼리가 실행된 직후 실제 DB내 시퀀스는 2로 바뀜)
                        "SELECT nextval('classroomnotification_notification_id_seq');";
                    roomNotification.notification_id = connection.QuerySingle<int>(sql: query1, transaction: transaction);
                    _logger.LogInformation($"InsertNotificationWithStudent?room_id={roomNotification.room_id}; notification_id={roomNotification.notification_id})");

                    string query2 = // ClassRoomNotification 테이블에 데이터를 INSERT
                        "INSERT INTO classroomnotification (room_id, notification_id, message, uri, notify_date) " +
                        "VALUES (@room_id, @notification_id, @message, @uri, @notify_date);";
                    var query2_params = new DynamicParameters();
                    query2_params.Add("room_id", roomNotification.room_id);
                    query2_params.Add("notification_id", roomNotification.notification_id);
                    query2_params.Add("message", roomNotification.message);
                    query2_params.Add("uri", roomNotification.uri);
                    query2_params.Add("notify_date", roomNotification.notify_date);
                    connection.Execute(query2, query2_params);

                    string query3 = // 해당 ClassRoom에서 강의를 수강 중인 모든 학생의 학번을 SELECT
                        "SELECT student_id " +
                        "FROM student " +
                        "WHERE room_id = @room_id;";
                    var query3_params = new DynamicParameters();
                    query3_params.Add("room_id", roomNotification.room_id);
                    var students = connection.Query<int>(query3, query3_params);

                    foreach(var student_id in students) {
                        string query4 = // 학번별로 StudentNotification 테이블에 데이터를 INSERT
                            "INSERT INTO studentnotification (room_id, student_id, notification_id, is_read) " +
                            "VALUES (@room_id, @student_id, @notification_id, @is_read);";
                        var query4_params = new DynamicParameters();
                        query4_params.Add("room_id", roomNotification.room_id);
                        query4_params.Add("student_id", student_id);
                        query4_params.Add("notification_id", roomNotification.notification_id);
                        query4_params.Add("is_read", false);
                        connection.Execute(query4, query4_params);
                    }

                    // 모든 쿼리가 성공적으로 실행되면 트랜잭션 커밋
                    transaction.Commit();
                    _logger.LogInformation($"{roomNotification.room_id}번 강의실의 {roomNotification.notification_id}번 알림 INSERT");
                } catch(Exception ex) {
                    _logger.LogError("알림을 INSERT 하던 중 문제가 발생하여 RollBack 합니다.");
                    _logger.LogError($"msg :\n{ex.Message}");
                    transaction.Rollback();
                }
            }
        }

        // 실제 요청 url 예시 : 'api/classroom/notification/read?room_id=1&student_id=60182147&notification_id=1'
        [HttpPut("notification/read")]
        public void ReadStudentNotification([FromQuery] int room_id, [FromQuery] int student_id, [FromQuery] int notification_id) {
            using var connection = new NpgsqlConnection(connectionString);
            string query =
                $"UPDATE studentnotification " +
                $"SET is_read = true " +
                $"WHERE room_id = @room_id AND student_id = @student_id AND notification_id = @notification_id;";
            var parameters = new DynamicParameters();
            parameters.Add("room_id", room_id);
            parameters.Add("student_id", student_id);
            parameters.Add("notification_id", notification_id);
            connection.Execute(query, parameters);
        }
    }
}
