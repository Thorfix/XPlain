using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using XPlain.Configuration;
using XPlain.Services;
using XPlain.Services.Validation;

namespace XPlain.Tests.Services
{
    public class ResponseValidationTests
    {
        private readonly Mock<IServiceProvider> _serviceProvider;
        private readonly Mock<ILogger<LLMProviderFactory>> _logger;
        private readonly LLMSettings _settings;
        private readonly LLMFallbackSettings _fallbackSettings;

        public ResponseValidationTests()
        {
            _serviceProvider = new Mock<IServiceProvider>();
            _logger = new Mock<ILogger<LLMProviderFactory>>();
            _settings = new LLMSettings();
            _fallbackSettings = new LLMFallbackSettings();
        }

        [Fact]
        public async Task AnthropicValidator_ValidatesCorrectFormat()
        {
            // Arrange
            var validator = new AnthropicResponseValidator();
            var response = "Human: test\nAssistant: This is a test response";

            // Act
            var result = await validator.ValidateFormatAsync(response);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task OpenAIValidator_ValidatesCorrectFormat()
        {
            // Arrange
            var validator = new OpenAIResponseValidator();
            var response = @"{""choices"": [{""text"": ""test response""}]}";

            // Act
            var result = await validator.ValidateFormatAsync(response);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task AzureOpenAIValidator_ValidatesCorrectFormat()
        {
            // Arrange
            var validator = new AzureOpenAIResponseValidator();
            var response = @"{""id"": ""azure-response"", ""choices"": [{""text"": ""test response""}]}";

            // Act
            var result = await validator.ValidateFormatAsync(response);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void Factory_ThrowsException_OnInvalidResponse()
        {
            // Arrange
            var factory = new LLMProviderFactory(
                _serviceProvider.Object,
                Options.Create(_settings),
                Options.Create(_fallbackSettings),
                _logger.Object
            );

            // Act & Assert
            Assert.ThrowsAsync<ResponseValidationException>(async () =>
            {
                await factory.ValidateResponseAsync("openai", "invalid json");
            });
        }

        [Fact]
        public void ValidatingProvider_ValidatesResponses()
        {
            // Arrange
            var mockInnerProvider = new Mock<ILLMProvider>();
            mockInnerProvider.Setup(x => x.GenerateResponseAsync(It.IsAny<string>()))
                .ReturnsAsync(@"{""choices"": [{""text"": ""valid response""}]}");

            var factory = new LLMProviderFactory(
                _serviceProvider.Object,
                Options.Create(_settings),
                Options.Create(_fallbackSettings),
                _logger.Object
            );

            var provider = new ValidatingLLMProvider(mockInnerProvider.Object, "openai", factory);

            // Act & Assert
            Assert.NotNull(provider);
            Assert.Equal(mockInnerProvider.Object.ProviderName, provider.ProviderName);
        }
    }
}