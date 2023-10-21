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
using System.Linq;

namespace YourNamespace
{
  public class UserVerificationEntity : ITableEntity
  {
    public string PartitionKey { get; set; }
    public string RowKey { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public string EMAIL { get; set; }
    public string INVITATION_CODE { get; set; }
  }

  public static class KptnVerifyInvitationCode
  {
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

      var invitationCodeAttributeKey = $"extension_{Environment.GetEnvironmentVariable("B2C_EXTENSIONS_APP_ID").Replace("-", "")}_InvitationCode";
      var requestBody = new System.IO.StreamReader(req.Body).ReadToEnd();
      var jsonData = JObject.Parse(requestBody);
      var invitationCode = jsonData[invitationCodeAttributeKey]?.ToString();
      var email = jsonData["email"]?.ToString();

      if (string.IsNullOrEmpty(invitationCode))
      {
        return new BadRequestObjectResult(new
        {
          version = "1.0.0",
          status = 400,
          action = "ValidationError",
          userMessage = "Please provide an invitation code."
        });
      }

      // get invitation code from azure storage table
      string tableName = "UserVerificationCode";
      string storageAccountConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");

      // Create a TableClient
      TableClient tableClient = new(storageAccountConnectionString, tableName);
      // Define the query
      Expression<Func<UserVerificationEntity, bool>> filterExpression = entity =>
          entity.EMAIL == email &&
          entity.INVITATION_CODE == invitationCode;

      string filter = TableClient.CreateQueryFilter(filterExpression);
      
      // Execute the query
      var entity = await tableClient.QueryAsync<UserVerificationEntity>(filter).FirstOrDefaultAsync();

      if (entity == null || entity.INVITATION_CODE != invitationCode || entity.EMAIL != email)
      {
        return new BadRequestObjectResult(new
        {
          version = "1.0.0",
          status = 400,
          action = "ValidationError",
          userMessage = "Please provide correct e-mail and invitation code."
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
