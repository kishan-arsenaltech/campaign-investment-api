namespace Investment.Core.Entities
{
    public class JwtConfig
    {
        public string JwtConfigName { get; set; }
        public string JwtSecret { get; set; }
        public string JwtExpiresIn { get; set; }

        public JwtConfig(string secretJwtNameValue, string secretJwtSecreValue, string secretJwtExpiresIn)
        {
            JwtConfigName = secretJwtNameValue;
            JwtSecret = secretJwtSecreValue;
            JwtExpiresIn = secretJwtExpiresIn;
        }
    }
}
