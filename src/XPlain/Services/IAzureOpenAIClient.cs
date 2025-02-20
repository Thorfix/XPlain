using System.Threading.Tasks;

namespace XPlain.Services
{
    public interface IAzureOpenAIClient : ILLMProvider
    {
        string Endpoint { get; }
        string DeploymentId { get; }
        string ApiVersion { get; }
        Task<string> CompletePromptAsync(string prompt);
        Task<string> StreamCompletionAsync(string prompt);
    }
}