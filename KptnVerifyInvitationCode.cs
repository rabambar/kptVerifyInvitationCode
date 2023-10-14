
using Microsoft.Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Azure;
using System.Threading.Tasks;
using System.Linq.Expressions;

namespace YourNamespace
{
    public class UserVerificationEntity  : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }
        public string Email { get; set; }
        public string VerificationCode { get; set; }
    }

    public static class KptnVerifyInvitationCode
    {
        private static readonly string[] ValidInvitationCodes = { "invitation-code-1", "invitation-code-2" };

        [FunctionName("KptnVerifyInvitationCode")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string authHeader = req.Headers["Authorization"];
            if (string.IsNullOrEmpty(authHeader))
            {
                log.LogWarning("Invalid Authentication");
                return new UnauthorizedResult();
            }

            var authParts = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Split(' ')[1])).Split(':');
            var username = authParts[0];
            var password = authParts[1];

            if (username != Environment.GetEnvironmentVariable("BASIC_AUTH_USERNAME") ||
                password != Environment.GetEnvironmentVariable("BASIC_AUTH_PASSWORD"))
            {
                log.LogWarning("Invalid Authentication");
                return new UnauthorizedResult();
            }

            // get invitation code from table
            string tableName = "UserVerificationCode";
            string storageAccountConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

            // Create a TableClient
            TableClient tableClient = new(storageAccountConnectionString, tableName);

            // Define the query
            Expression<Func<UserVerificationEntity, bool>> filterExpression = entity =>
                entity.Email == "example@example.com" &&
                entity.VerificationCode == "123456";

            string filter = TableClient.CreateQueryFilter(filterExpression);

            // Execute the query
            await foreach (TableEntity entity in tableClient.QueryAsync<TableEntity>(filter))
            {
                log.LogInformation($"Entity found: {entity["EMAIL"]}, {entity["VERIFICATION_CODE"]}");
            }

            var invitationCodeAttributeKey = $"extension_{Environment.GetEnvironmentVariable("B2C_EXTENSIONS_APP_ID")}_InvitationCode";
            var requestBody = new System.IO.StreamReader(req.Body).ReadToEnd();
            var jsonData = JObject.Parse(requestBody);
            var inviteCode = jsonData[invitationCodeAttributeKey]?.ToString();

            if (string.IsNullOrEmpty(inviteCode))
            {
                return new BadRequestObjectResult(new
                {
                    version = "1.0.0",
                    status = 400,
                    action = "ValidationError",
                    userMessage = "Please provide an invitation code."
                });
            }

            if (!Array.Exists(ValidInvitationCodes, code => code.Equals(inviteCode)))
            {
                return new BadRequestObjectResult(new
                {
                    version = "1.0.0",
                    status = 400,
                    action = "ValidationError",
                    userMessage = "Your invitation code is invalid. Please try again."
                });
            }

            return new OkObjectResult(new Dictionary<string, object>
            {
                ["version"] = "1.0.0",
                ["action"] = "Continue",
                [invitationCodeAttributeKey] = ""
            });
        }
    }
}
