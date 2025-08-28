using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Data.SqlClient;

namespace api
{
    public static class RegisterVisitor
    {
        [FunctionName("RegisterVisitor")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("RegisterVisitor function triggered.");

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            string name = data?.name;
            string email = data?.email;
            string purpose = data?.purpose;

            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email))
            {
                return new BadRequestObjectResult("Name and Email are required.");
            }

            string connStr = Environment.GetEnvironmentVariable("SqlConnectionString");

            try
            {
                using (SqlConnection conn = new SqlConnection(connStr))
                {
                    await conn.OpenAsync();

                    // Check if visitor exists
                    int visitorId;
                    using (SqlCommand checkCmd = new SqlCommand(
                        "SELECT VisitorID FROM Visitors WHERE Email=@Email", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@Email", email);
                        var result = await checkCmd.ExecuteScalarAsync();
                        if (result == null)
                        {
                            // Insert new visitor
                            using (SqlCommand insertVisitor = new SqlCommand(
                                "INSERT INTO Visitors (FullName, Email) OUTPUT INSERTED.VisitorID VALUES (@FullName, @Email)", conn))
                            {
                                insertVisitor.Parameters.AddWithValue("@FullName", name);
                                insertVisitor.Parameters.AddWithValue("@Email", email);
                                visitorId = (int)await insertVisitor.ExecuteScalarAsync();
                            }
                        }
                        else
                        {
                            visitorId = (int)result;
                        }
                    }

                    // Insert visit
                    using (SqlCommand insertVisit = new SqlCommand(
                        "INSERT INTO Visits (VisitorID, Purpose) VALUES (@VisitorID, @Purpose)", conn))
                    {
                        insertVisit.Parameters.AddWithValue("@VisitorID", visitorId);
                        insertVisit.Parameters.AddWithValue("@Purpose", purpose ?? (object)DBNull.Value);
                        await insertVisit.ExecuteNonQueryAsync();
                    }
                }

                log.LogInformation($"Visitor registered: {name} ({email}) - {purpose}");
                return new OkObjectResult($"Welcome {name}, your visit has been registered.");
            }
            catch (Exception ex)
            {
                log.LogError($"Error registering visitor: {ex.Message}");
                return new StatusCodeResult(500);
            }
        }
    }
}
