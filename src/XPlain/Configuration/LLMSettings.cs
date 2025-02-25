using System;
using System.ComponentModel.DataAnnotations;

namespace XPlain.Configuration
{
    public class LLMSettings
    {
        [Required(ErrorMessage = "Provider is required")]
        public virtual string Provider { get; set; }
        
        [Required(ErrorMessage = "Model is required")]
        public string Model { get; set; }
        
        [Required(ErrorMessage = "API key is required")]
        public string ApiKey { get; set; }
        
        [Range(5, 300, ErrorMessage = "Timeout must be between 5 and 300 seconds")]
        public virtual int TimeoutSeconds { get; set; } = 30;
        
        public LLMFallbackSettings Fallback { get; set; }
        
        public virtual void Validate()
        {
            if (string.IsNullOrWhiteSpace(Provider))
                throw new ValidationException("Provider is required");
                
            if (string.IsNullOrWhiteSpace(Model))
                throw new ValidationException("Model is required");
                
            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new ValidationException("API key is required");
                
            if (TimeoutSeconds < 5 || TimeoutSeconds > 300)
                throw new ValidationException("Timeout must be between 5 and 300 seconds");
        }
    }
}