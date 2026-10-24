namespace MovieLibrary.Core.Tmdb;

public enum TmdbAuthMode { BearerToken, QueryKey }

public static class TmdbAuth
{
    /// <summary>
    /// v4 read access tokens are JWTs and begin "eyJ"; v3 API keys are bare hex.
    /// The v4 token is preferred because it travels in a header rather than a URL.
    /// </summary>
    public static TmdbAuthMode Detect(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("TMDB_TOKEN is empty. Set it in .env locally, or as an " +
                                        "Actions repository secret in CI.", nameof(token));

        return token.Trim().StartsWith("eyJ", StringComparison.Ordinal)
            ? TmdbAuthMode.BearerToken
            : TmdbAuthMode.QueryKey;
    }
}
