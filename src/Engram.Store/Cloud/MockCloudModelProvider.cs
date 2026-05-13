namespace Engram.Store.Cloud;

/// <summary>
/// Mock cloud model provider for testing and development.
/// Returns predictable responses without making real API calls.
/// </summary>
public class MockCloudModelProvider : ICloudModelProvider
{
    private readonly decimal _costPerCall;
    private readonly int _inputTokens;
    private readonly int _outputTokens;
    private bool _shouldFail;
    private string? _failureMessage;

    public MockCloudModelProvider(
        decimal costPerCall = 0.001m,
        int inputTokens = 100,
        int outputTokens = 50)
    {
        _costPerCall = costPerCall;
        _inputTokens = inputTokens;
        _outputTokens = outputTokens;
    }

    public bool IsAvailable { get; set; } = true;
    public string ProviderName => "mock";
    public string ModelName => "mock-model";

    /// <summary>Configure the mock to fail on the next call.</summary>
    public void ConfigureFailure(string message = "Mock failure")
    {
        _shouldFail = true;
        _failureMessage = message;
    }

    /// <summary>Reset failure state.</summary>
    public void ResetFailure()
    {
        _shouldFail = false;
        _failureMessage = null;
    }

    public Task<CloudModelResponse> SendAsync(CloudModelRequest request, CancellationToken cancellationToken = default)
    {
        if (_shouldFail)
        {
            return Task.FromResult(new CloudModelResponse
            {
                Content = string.Empty,
                Provider = ProviderName,
                Model = ModelName,
                CostEstimate = 0,
                InputTokens = 0,
                OutputTokens = 0,
                Success = false,
                ErrorMessage = _failureMessage
            });
        }

        return Task.FromResult(new CloudModelResponse
        {
            Content = $"Mock response to: {request.Reason}",
            Provider = ProviderName,
            Model = ModelName,
            CostEstimate = _costPerCall,
            InputTokens = _inputTokens,
            OutputTokens = _outputTokens,
            Success = true
        });
    }
}
