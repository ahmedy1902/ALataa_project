using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;

namespace Accounts.Services
{
    public class ArcGisResponse<T> { public List<ArcGisFeature<T>> features { get; set; } }
    public class ArcGisFeature<T> { public T attributes { get; set; } public Geometry geometry { get; set; } }
    public class Geometry { public double x { get; set; } public double y { get; set; } }

    public class CharityFeature
    {
        public int? objectid { get; set; }
        public string charity_name { get; set; }
        public string charity_sector { get; set; }
        public double? how_much_do_you_need { get; set; }
        public string enter_your_e_mail { get; set; } // إضافة حقل الإيميل
        public double? x { get; set; } // Longitude
        public double? y { get; set; } // Latitude
    }

    public class NeedyFeature
    {
        public int? objectid { get; set; }
        public string full_name { get; set; }
        public string type_of_need { get; set; }
        public double? how_much_do_you_need { get; set; }
        public string email { get; set; } // إضافة حقل الإيميل
        public double? x { get; set; } // Longitude
        public double? y { get; set; } // Latitude
    }

    public class DonationFeature
    {
        public string donor_email { get; set; }
        public string recipient_email { get; set; }
        public string recipient_name { get; set; } // إضافة اسم المستفيد
        public string donation_field { get; set; }
        public double donation_amount { get; set; }
        public DateTime donation_date { get; set; }
        public double donor_x { get; set; }
        public double donor_y { get; set; }
        public double recipient_x { get; set; }
        public double recipient_y { get; set; }
    }

    public class ArcGisDonation
    {
        public string donor_email { get; set; }
        public string recipient_email { get; set; }
        public string recipient_name { get; set; }
        public string donation_field { get; set; }
        public double? donation_amount { get; set; }
        public long? donation_date { get; set; } // Epoch milliseconds
        public double? donor_x { get; set; }
        public double? donor_y { get; set; }
        public double? recipient_x { get; set; }
        public double? recipient_y { get; set; }
    }

    public class ArcGisService
    {
        private readonly HttpClient _client;
        private readonly ILogger<ArcGisService>? _logger;

        public ArcGisService(HttpClient client, ILogger<ArcGisService>? logger = null)
        {
            _client = client;
            _logger = logger;
        }

        public async Task<List<CharityFeature>> GetCharitiesAsync()
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea/FeatureServer/0/query?where=1=1&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            return data?.features?.Select(f =>
            {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).ToList() ?? new();
        }

        public async Task<List<NeedyFeature>> GetNeediesAsync()
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_1b6326b33d2b4213bf757d6780a0f12a/FeatureServer/0/query?where=1=1&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<NeedyFeature>>(response);
            return data?.features?.Select(f =>
            {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).ToList() ?? new();
        }

        public async Task<bool> AddDonationAsync(DonationFeature donation)
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/Donations_made_by_donors/FeatureServer/0/addFeatures";
            var featuresArr = new[]
            {
                new
                {
                    attributes = new Dictionary<string, object>
                    {
                        ["donor_email"] = donation.donor_email ?? string.Empty,
                        ["recipient_email"] = donation.recipient_email ?? string.Empty,
                        ["recipient_name"] = donation.recipient_name ?? string.Empty,
                        ["donation_field"] = donation.donation_field ?? string.Empty,
                        ["donation_amount"] = donation.donation_amount,
                        ["donation_date"] = ((DateTimeOffset)donation.donation_date).ToUnixTimeMilliseconds(),
                        ["donor_x"] = donation.donor_x,
                        ["donor_y"] = donation.donor_y,
                        ["recipient_x"] = donation.recipient_x,
                        ["recipient_y"] = donation.recipient_y
                    },
                    geometry = new { x = donation.donor_x, y = donation.donor_y, spatialReference = new { wkid = 4326 } }
                }
            };
            var featuresJson = JsonSerializer.Serialize(featuresArr);
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("features", featuresJson),
                new KeyValuePair<string, string>("f", "json")
            };
            var content = new FormUrlEncodedContent(form);
            var response = await _client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"ArcGIS AddDonationAsync response: {responseContent}");
            System.Diagnostics.Debug.WriteLine($"ArcGIS AddDonationAsync response: {responseContent}");
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"ArcGIS AddDonationAsync failed: {responseContent}");
            }
            return responseContent.Contains("success");
        }

        // تحديث المبلغ المطلوب للجمعية الخيرية
        public async Task<bool> UpdateCharityNeededAmountAsync(int objectId, double newNeededAmount)
        {
            try
            {
                var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea/FeatureServer/0/updateFeatures";

                var featuresArr = new[]
                {
                    new
                    {
                        attributes = new Dictionary<string, object>
                        {
                            ["objectid"] = objectId,
                            ["how_much_do_you_need"] = newNeededAmount
                        }
                    }
                };

                var featuresJson = JsonSerializer.Serialize(featuresArr);
                var form = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("features", featuresJson),
                    new KeyValuePair<string, string>("f", "json")
                };

                var content = new FormUrlEncodedContent(form);
                var response = await _client.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"UpdateCharityNeededAmount response for ObjectId {objectId}: {responseContent}");
                _logger?.LogInformation($"UpdateCharityNeededAmount response for ObjectId {objectId}: {responseContent}");

                return response.IsSuccessStatusCode && (responseContent.Contains("success") || responseContent.Contains("updateResults"));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error updating charity needed amount for ObjectId {objectId}");
                return false;
            }
        }

        // تحديث المبلغ المطلوب للمحتاج
        public async Task<bool> UpdateNeedyNeededAmountAsync(int objectId, double newNeededAmount)
        {
            try
            {
                var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_1b6326b33d2b4213bf757d6780a0f12a/FeatureServer/0/updateFeatures";

                var featuresArr = new[]
                {
                    new
                    {
                        attributes = new Dictionary<string, object>
                        {
                            ["objectid"] = objectId,
                            ["how_much_do_you_need"] = newNeededAmount
                        }
                    }
                };

                var featuresJson = JsonSerializer.Serialize(featuresArr);
                var form = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("features", featuresJson),
                    new KeyValuePair<string, string>("f", "json")
                };

                var content = new FormUrlEncodedContent(form);
                var response = await _client.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"UpdateNeedyNeededAmount response for ObjectId {objectId}: {responseContent}");
                _logger?.LogInformation($"UpdateNeedyNeededAmount response for ObjectId {objectId}: {responseContent}");

                return response.IsSuccessStatusCode && (responseContent.Contains("success") || responseContent.Contains("updateResults"));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error updating needy needed amount for ObjectId {objectId}");
                return false;
            }
        }

        public async Task<bool> UpdateDonorNeededAmountAsync(string email, double newNeededAmount)
        {
            // تحديث needed amount في لاير المتبرعين
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_fb464f56faae4b6c803825277c69be1c_results/FeatureServer/0/updateFeatures";
            // جلب objectid للمتبرع
            var donor = await GetDonorByEmailAsync(email);
            if (donor == null || donor.objectid == null)
                return false;

            var featuresArr = new[]
            {
                new
                {
                    attributes = new Dictionary<string, object>
                    {
                        ["objectid"] = donor.objectid,
                        ["how_much_do_you_need"] = newNeededAmount
                    }
                }
            };

            var featuresJson = JsonSerializer.Serialize(featuresArr);
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("features", featuresJson),
                new KeyValuePair<string, string>("f", "json")
            };

            var content = new FormUrlEncodedContent(form);
            var response = await _client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && (responseContent.Contains("success") || responseContent.Contains("updateResults"));
        }

        // تحديث needed amount للمستفيد (charity أو needy) - deprecated, use specific methods above
        public async Task<bool> UpdateBeneficiaryNeededAmountAsync(string beneficiaryType, int objectId, double newNeededAmount)
        {
            if (beneficiaryType == "Charity")
            {
                return await UpdateCharityNeededAmountAsync(objectId, newNeededAmount);
            }
            else if (beneficiaryType == "Beneficiary")
            {
                return await UpdateNeedyNeededAmountAsync(objectId, newNeededAmount);
            }
            return false;
        }

        public async Task<List<ArcGisDonation>> GetDonationsAsync(string donorEmail)
        {
            var url = $"https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/Donations_made_by_donors/FeatureServer/0/query?where=donor_email='{donorEmail}'&outFields=*&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<ArcGisDonation>>(response);
            return data?.features?.Select(f => f.attributes).ToList() ?? new();
        }

        public async Task<CharityFeature> GetDonorByEmailAsync(string email)
        {
            var url = $"https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_fb464f56faae4b6c803825277c69be1c_results/FeatureServer/0/query?where=enter_your_e_mail='{email}'&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            return data?.features?.Select(f =>
            {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).FirstOrDefault();
        }

        // البحث عن الجمعية الخيرية بالإيميل
        public async Task<CharityFeature> GetCharityByEmailAsync(string email)
        {
            var url = $"https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea/FeatureServer/0/query?where=enter_your_e_mail='{email}'&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            return data?.features?.Select(f =>
            {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).FirstOrDefault();
        }

        // البحث عن المحتاج بالإيميل
        public async Task<NeedyFeature> GetNeedyByEmailAsync(string email)

        {

            var url = $"https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_1b6326b33d2b4213bf757d6780a0f12a/FeatureServer/0/query?where=email='{email.Replace("'", "''")}'&outFields=*&returnGeometry=true&f=json";

            var response = await _client.GetStringAsync(url);

            var data = JsonSerializer.Deserialize<ArcGisResponse<NeedyFeature>>(response);

            return data?.features?.Select(f => {

                f.attributes.x = f.geometry?.x;

                f.attributes.y = f.geometry?.y;

                return f.attributes;

            }).FirstOrDefault();

        }

        // Send charity registration data to ArcGIS Feature Layer
        public async Task<bool> SendCharityDataAsync(string charityName, string charitySector, string numberOfCasesSponsoredMonth, string monthlyDonationAmount, double howMuchDoYouNeed, string email, double x, double y)
        {
            // تم تصحيح الرابط ليشمل _results
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea/FeatureServer/0/addFeatures";
            var registrationDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var payload = new
            {
                features = new[] {
                    new {
                        attributes = new {
                            charity_name = charityName,
                            charity_sector = charitySector,
                            field_9 = numberOfCasesSponsoredMonth,
                            field_10 = monthlyDonationAmount,
                            how_much_do_you_need = howMuchDoYouNeed,
                            enter_your_e_mail = email,
                            registration_date = registrationDate
                        },
                        geometry = new {
                            x = x,
                            y = y,
                            spatialReference = new { wkid = 4326 }
                        }
                    }
                },
                f = "json"
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && responseContent.Contains("addResults");
        }

        // Send charity registration data to ArcGIS Feature Layer (overload for RegisterModel)
        public async Task<bool> SendCharityDataAsync(Accounts.ViewModels.RegisterModel model)
        {
            try
            {
                // تحويل CharitySector إلى نص مفصول بفواصل إذا كانت قائمة
                string charitySectorStr = model.CharitySector is List<string> list ? string.Join(",", list) : (model.CharitySector?.ToString() ?? string.Empty);
                return await SendCharityDataAsync(
                    model.CharityName ?? string.Empty,
                    charitySectorStr,
                    model.CasesSponsored ?? string.Empty,
                    model.MonthlyDonation ?? string.Empty,
                    model.CharityNeededAmount ?? 0,
                    model.Email,
                    model.Longitude ?? 0,
                    model.Latitude ?? 0
                );
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error sending charity data for email: {Email}", model.Email);
                throw;
            }
        }
        public async Task<bool> UpdateNeedyByEmailAsync(string email, double newNeededAmount)
        {
            try
            {
                var baseUrl = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_1b6326b33d2b4213bf757d6780a0f12a/FeatureServer/0";
                var whereClause = $"email = '{email.Replace("'", "''")}'";
                var queryUrl = $"{baseUrl}/query?f=json&where={Uri.EscapeDataString(whereClause)}&outFields=objectid";

                var queryResponse = await _client.GetStringAsync(queryUrl);
                var queryResult = JsonSerializer.Deserialize<ArcGisResponse<NeedyFeature>>(queryResponse);
                var objectId = queryResult?.features?.FirstOrDefault()?.attributes?.objectid;

                if (objectId == null) return false;

                var updatePayload = new[]
                {
            new {
                attributes = new Dictionary<string, object>
                {
                    { "objectid", objectId },
                    { "how_much_do_you_need", newNeededAmount }
                }
            }
        };

                var featuresJson = JsonSerializer.Serialize(updatePayload);
                var form = new List<KeyValuePair<string, string>>
        {
            new("features", featuresJson),
            new("f", "json")
        };

                var content = new FormUrlEncodedContent(form);
                var updateResponse = await _client.PostAsync($"{baseUrl}/updateFeatures", content);
                var responseContent = await updateResponse.Content.ReadAsStringAsync();

                _logger?.LogInformation($"UpdateNeedyByEmailAsync for {email}: {responseContent}");
                return updateResponse.IsSuccessStatusCode && responseContent.Contains("success");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Error updating needy by email: {email}");
                return false;
            }
        }
        public async Task<bool> UpdateCharityByEmailAsync(string email, double newNeededAmount)
        {
            try
            {
                var baseUrl = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea/FeatureServer/0";
                var whereClause = $"enter_your_e_mail='{email.Replace("'", "''")}'";
                var queryUrl = $"{baseUrl}/query?f=json&where={Uri.EscapeDataString(whereClause)}&outFields=objectid";

                var queryResponse = await _client.GetStringAsync(queryUrl);
                var queryResult = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(queryResponse);
                var objectId = queryResult?.features?.FirstOrDefault()?.attributes?.objectid;

                if (objectId == null) return false;

                var updatePayload = new[]
                {
            new
            {
                attributes = new Dictionary<string, object>
                {
                    ["objectid"] = objectId,
                    ["how_much_do_you_need"] = newNeededAmount
                }
            }
        };

                var featuresJson = JsonSerializer.Serialize(updatePayload);
                var form = new List<KeyValuePair<string, string>>
        {
            new("features", featuresJson),
            new("f", "json")
        };

                var content = new FormUrlEncodedContent(form);
                var updateResponse = await _client.PostAsync($"{baseUrl}/updateFeatures", content);
                var responseContent = await updateResponse.Content.ReadAsStringAsync();

                _logger?.LogInformation($"✅ UpdateCharityByEmailAsync for {email}: {responseContent}");
                return updateResponse.IsSuccessStatusCode && responseContent.Contains("success");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"❌ Error updating charity by email: {email}");
                return false;
            }
        }


    }
}