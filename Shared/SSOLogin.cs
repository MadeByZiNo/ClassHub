namespace ClassHub.Shared
{
    public class AccessTokenRequest
    {
        public string? AuthorizationCode { get; set; }
    }

    public class AccessTokenResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }
}