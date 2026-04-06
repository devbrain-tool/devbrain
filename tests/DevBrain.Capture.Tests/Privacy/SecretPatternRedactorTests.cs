namespace DevBrain.Capture.Tests.Privacy;

using DevBrain.Capture.Privacy;

public class SecretPatternRedactorTests
{
    private readonly SecretPatternRedactor _redactor = new();

    [Fact]
    public void Redacts_GitHub_PAT()
    {
        var content = "token: ghp_ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmn";
        var result = _redactor.Redact(content);
        Assert.Contains("[REDACTED:github-pat]", result);
        Assert.DoesNotContain("ghp_", result);
    }

    [Fact]
    public void Redacts_OpenAI_API_Key()
    {
        var content = "OPENAI_API_KEY=sk-abc123def456ghi789jkl012mno";
        var result = _redactor.Redact(content);
        Assert.Contains("[REDACTED:api-key]", result);
        Assert.DoesNotContain("sk-abc", result);
    }

    [Fact]
    public void Redacts_Bearer_Token()
    {
        var content = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.payload.signature";
        var result = _redactor.Redact(content);
        Assert.Contains("[REDACTED:bearer-token]", result);
        Assert.DoesNotContain("eyJhbG", result);
    }

    [Fact]
    public void Redacts_PEM_Private_Key()
    {
        var content = "-----BEGIN PRIVATE KEY-----\nMIIEvgIBADANBg...\n-----END PRIVATE KEY-----";
        var result = _redactor.Redact(content);
        Assert.Contains("[REDACTED:private-key]", result);
        Assert.DoesNotContain("MIIEvg", result);
    }

    [Fact]
    public void Redacts_RSA_Private_Key()
    {
        var content = "-----BEGIN RSA PRIVATE KEY-----\nMIIEvgIBADANBg...\n-----END RSA PRIVATE KEY-----";
        var result = _redactor.Redact(content);
        Assert.Contains("[REDACTED:private-key]", result);
    }

    [Fact]
    public void Redacts_Generic_ApiKey_Pattern()
    {
        var content = "api_key=mysupersecretkey123456";
        var result = _redactor.Redact(content);
        Assert.Contains("[REDACTED:secret]", result);
        Assert.DoesNotContain("mysupersecretkey", result);
    }

    [Fact]
    public void Redacts_Generic_Password_Pattern()
    {
        var content = "password: \"mysecretpassword99\"";
        var result = _redactor.Redact(content);
        Assert.Contains("[REDACTED:secret]", result);
        Assert.DoesNotContain("mysecretpassword", result);
    }

    [Fact]
    public void Leaves_NonSecret_Content_Unchanged()
    {
        var content = "This is a normal code comment about a function that returns a list.";
        var result = _redactor.Redact(content);
        Assert.Equal(content, result);
    }

    [Fact]
    public void Leaves_Normal_Code_Unchanged()
    {
        var content = "var items = db.Query(\"SELECT * FROM users WHERE active = true\");";
        var result = _redactor.Redact(content);
        Assert.Equal(content, result);
    }
}
