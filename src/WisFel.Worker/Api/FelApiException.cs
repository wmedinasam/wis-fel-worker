using System.Net;

namespace WisFel.Worker.Api;

public class FelApiException : Exception
{
    public FelApiException(string endpoint, string companyKey, HttpStatusCode? statusCode, string message)
        : base(message)
    {
        Endpoint = endpoint;
        CompanyKey = companyKey;
        StatusCode = statusCode;
    }

    public FelApiException(
        string endpoint,
        string companyKey,
        HttpStatusCode? statusCode,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Endpoint = endpoint;
        CompanyKey = companyKey;
        StatusCode = statusCode;
    }

    public string Endpoint { get; }

    public string CompanyKey { get; }

    public HttpStatusCode? StatusCode { get; }
}

/// <summary>A 403 from the agent endpoints means an invalid license, not bad credentials.</summary>
public sealed class FelApiLicenseException(string endpoint, string companyKey)
    : FelApiException(
        endpoint,
        companyKey,
        HttpStatusCode.Forbidden,
        $"The API rejected company '{companyKey}' on {endpoint} because its license is not valid.");
