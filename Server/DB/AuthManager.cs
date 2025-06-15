using System;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Newtonsoft.Json;
namespace Server.DB;

// Firebase 로그인 응답 클래스
public class FirebaseSignInResponse
{
    public string localId      { get; set; }
    public string email        { get; set; }
    public string displayName  { get; set; }
    public string idToken      { get; set; }
    public string refreshToken { get; set; }
    public int    expiresIn    { get; set; }
}

public class AuthManager
{
    // Firebase Admin Auth 인스턴스
    private FirebaseAuth auth;
    private FirebaseApp  app;
    
    // HTTP 클라이언트 (Firebase REST API용)
    private static readonly HttpClient httpClient = new HttpClient();
    private const string FIREBASE_API_KEY = "AIzaSyADRu3FNT5_KYmFDSR65J4jEUlp5ee9v9o";
    
    // 생성자
    public AuthManager()
    {
        try
        {
            // Firebase Admin SDK 초기화
            string serviceAccountPath = @"C:\Users\ASUS\Desktop\Unity\Project\3D_RPG_Server(Git)\Firebase\d-rpg-server-firebase-adminsdk-fbsvc-cc3363d61c.json";
            
            if (FirebaseApp.DefaultInstance == null)
            {
                app = FirebaseApp.Create(new AppOptions()
                {
                    Credential = GoogleCredential.FromFile(serviceAccountPath),
                    ProjectId = "d-rpg-server"
                });
            }
            else
            {
                app = FirebaseApp.DefaultInstance;
            }
            
            auth = FirebaseAuth.GetAuth(app);
            
            //Console.WriteLine("Firebase Auth 초기화 완료");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Firebase Auth 초기화 실패: {ex.Message}");
        }
    }
    
    // 아이디 생성 처리
    public async void MakeIdAsync(string email, string password, string nickname, string serialNumber, ClientSession session)
    {
        try
        {
            var userArgs = new UserRecordArgs()
            {
                Email    = email,      // 기본정보 세팅
                Password = password,   // 기본정보 세팅
            };
            
            // Auth 저장
            var userRecord = await auth.CreateUserAsync(userArgs);
            Console.WriteLine($"아이디 생성성 완료 : {userRecord.Uid}");
            
            // 성공 결과 클라에게 보내주기
            S_MakeIdResult loginResult = new S_MakeIdResult
            {
                isSuccess  = true,
                resultText = "아이디 생성 성공!"
            };
            session.Send(loginResult.Write()); // Session의 Send를 통해, 단일로 바로 보내주기(단일로 보내주기 때문에 ID가 필요 없음.)

            // 리얼타임 데이터베이스에 기본 캐릭터 정보 생성
            await Program.DBManager._realTime.SaveDefaultDataAsync(email, nickname, serialNumber);
			Console.WriteLine($"사용자 데이터 리얼타임 데이터 베이스 저장 완료 : {userRecord.Uid}");
        }
        catch (FirebaseAuthException e)
        {
            Console.WriteLine($"사용자 생성 실패 : {e.Message}");
            // 성공 결과 클라에게 보내주기
            S_MakeIdResult loginResult = new S_MakeIdResult
            {
                isSuccess  = false,
                resultText = "아이디 생성 실패! " + e.Message
            };
            session.Send(loginResult.Write()); // Session의 Send를 통해, 단일로 바로 보내주기(단일로 보내주기 때문에 ID가 필요 없음.)
        }
        catch (Exception e)
        {
            Console.WriteLine($"기타 문제 : {e.Message}");
            // 성공 결과 클라에게 보내주기
            S_MakeIdResult loginResult = new S_MakeIdResult
            {
                isSuccess  = false,
                resultText = "아이디 생성 실패! " + e.Message
            };
            session.Send(loginResult.Write()); // Session의 Send를 통해, 단일로 바로 보내주기(단일로 보내주기 때문에 ID가 필요 없음.)
        }
    }
    
    // 로그인 처리
    public async void LoginAsync(string email, string password, ClientSession session)
    {
        try
        {
            // Firebase REST API로 이메일 존재와 패스워드가 맞는지 확인
            bool isLoginSuccess = await VerifyEmailPasswordAsync(email, password);
            
            // 로그인 성공
            if (isLoginSuccess)
            {
                Console.WriteLine($"로그인 성공: {email}");
                
                // 로그인에 성공하면, 이메일을 가지고, 리얼타임데이터베이스에서 정보를 가져온다.
                DefaultData userData = await Program.DBManager._realTime.GetUserDataAsync(email);
                
                if (userData != null)
                {
                    // 저장된 정보를 패킷에 담아서 클라에게 보내준다.
                    S_LoginResult loginResult = new S_LoginResult
                    {
                        isSuccess     = true,
                        resultText    = "로그인 성공!",
                        email         = userData.email,
                        creationDate  = userData.creationDate,
                        serialNumber  = userData.serialNumber,
                        nickname      = userData.nickname,
                        currentLevel  = int.Parse(userData.currentLevel),
                        currentHp     = int.Parse(userData.currentHp),
                        currentExp    = int.Parse(userData.currentExp),
                        currentGold   = int.Parse(userData.currentGold),
                        savedPosition = userData.savedPosition,
                        savedScene    = userData.savedScene,
                        previousScene = userData.previousScene
                    };
                    session.Send(loginResult.Write());
                    
                    // 저장된 정보로 내 세션에 세팅해준다.
					session.email         = userData.email;
                    session.SerialNumber  = userData.serialNumber;
                    session.NickName      = userData.nickname;
                    session.CurrentLevel  = int.Parse(userData.currentLevel);
                    session.CurrentHP     = int.Parse(userData.currentHp);
                    session.CurrentExp    = int.Parse(userData.currentExp);
                    session.CurrentGold   = int.Parse(userData.currentGold);
                    Vector3 savedPosition = Extension.ParseVector3(userData.savedPosition);
                    session.PosX = savedPosition.X;
                    session.PosY = savedPosition.Y;
                    session.PosZ = savedPosition.Z;
                    
                    // 캐릭터 정보를 통해 데미지 설정
                    CharacterInfoData characterInfo = Program.DBManager.GetCharacterInfo(userData.serialNumber, int.Parse(userData.currentLevel));
                    if (characterInfo != null)
                    {
                        session.Damage    = int.Parse(characterInfo.normalAttackDamage);
                        session.Body_Size = float.Parse(characterInfo.body_Size);
                        session.MaxHP     = int.Parse(characterInfo.maxHp);
                        session.moveSpeed = float.Parse(characterInfo.moveSpeed);
                    }
                    else
                    {
                        Console.WriteLine($"캐릭터 정보를 찾을 수 없습니다: {userData.serialNumber}_{userData.currentLevel}");
                    }
                    
                    Console.WriteLine($"사용자 데이터 로드 완료: {userData.nickname}");
                }
                else
                {
                    // 사용자 데이터가 없는 경우 (이론적으로는 발생하지 않아야 함)
                    Console.WriteLine($"로그인 성공했지만 사용자 데이터가 없음: {email}");
                    S_LoginResult loginResult = new S_LoginResult
                    {
                        isSuccess  = false,
                        resultText = "사용자 데이터를 찾을 수 없습니다."
                    };
                    session.Send(loginResult.Write());
                }
            }
            // 로그인 실패
            else
            {
                Console.WriteLine($"로그인 실패: 이메일 또는 비밀번호가 틀림");
                S_LoginResult loginResult = new S_LoginResult
                {
                    isSuccess  = false,
                    resultText = "이메일 또는 비밀번호가 잘못되었습니다."
                };
                session.Send(loginResult.Write());
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"로그인 오류: {e.Message}");
            S_LoginResult loginResult = new S_LoginResult
            {
                isSuccess  = false,
                resultText = "로그인 중 오류가 발생했습니다."+ e.Message
            };
            session.Send(loginResult.Write());
        }
    }
    
    // Firebase REST API를 사용한 이메일/비밀번호 검증
    private async Task<bool> VerifyEmailPasswordAsync(string email, string password)
    {
        try
        {
            // Firebase REST API로 로그인 시도
            var loginData = new
            {
                email             = email,
                password          = password,
                returnSecureToken = true
            };
            
            string jsonContent = JsonConvert.SerializeObject(loginData);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            string url = $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key={FIREBASE_API_KEY}";
            var response = await httpClient.PostAsync(url, content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var result = JsonConvert.DeserializeObject<FirebaseSignInResponse>(responseContent);
                
                Console.WriteLine($"Firebase 인증 성공: {result.localId}");
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Firebase 인증 실패: {errorContent}");
                return false;
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"인증 검증 오류: {e.Message}");
            return false;
        }
    }
}