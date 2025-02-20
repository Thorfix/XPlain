using System;

namespace XPlain.Services.Validation
{
    /// <summary>
    /// Exception thrown when response validation fails
    /// </summary>
    public class ResponseValidationException : Exception
    {
        /// <summary>
        /// Gets the type of validation that failed
        /// </summary>
        public ResponseValidationType ValidationType { get; }

        /// <summary>
        /// Creates a new instance of ResponseValidationException
        /// </summary>
        public ResponseValidationException(string message, ResponseValidationType type) 
            : base(message)
        {
            ValidationType = type;
        }

        /// <summary>
        /// Creates a new instance of ResponseValidationException with inner exception
        /// </summary>
        public ResponseValidationException(string message, ResponseValidationType type, Exception innerException) 
            : base(message, innerException)
        {
            ValidationType = type;
        }
    }

    /// <summary>
    /// Types of response validation
    /// </summary>
    public enum ResponseValidationType
    {
        Schema,
        Quality,
        Format,
        Error
    }
}