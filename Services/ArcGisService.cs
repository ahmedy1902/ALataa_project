using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Accounts.ViewModels;

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
        private readonly ILogger<ArcGisService> _logger;
        private readonly ArcGisSettings _settings;

        public ArcGisService(HttpClient client, IOptions<ArcGisSettings> settings, ILogger<ArcGisService> logger = null)
        {
            _client = client;
            _logger = logger;
            _settings = settings.Value;
        }

        public async Task<List<CharityFeature>> GetCharitiesAsync()
        {
            var url = $"{_settings.CharitiesServiceUrl}/query?where=1=1&outFields=*&returnGeometry=true&f=json";
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
            var url = $"{_settings.NeediesServiceUrl}/query?where=1=1&outFields=*&returnGeometry=true&f=json";
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
            var url = $"{_settings.DonationsLayerUrl}/addFeatures";
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
                var url = $"{_settings.CharitiesServiceUrl}/updateFeatures";

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

                if (!response.IsSuccessStatusCode || responseContent.Contains("\"error\""))
                {
                    _logger?.LogError($"ArcGIS Update Failed for Charity {objectId}: {responseContent}");
                    return false;
                }
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(responseContent))
                    {
                        if (doc.RootElement.TryGetProperty("updateResults", out JsonElement results) && results.GetArrayLength() > 0)
                        {
                            return results[0].GetProperty("success").GetBoolean();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, "Failed to parse JSON for UpdateCharityNeededAmountAsync");
                }
                return false;
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
                var url = $"{_settings.NeediesServiceUrl}/updateFeatures";

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

                if (!response.IsSuccessStatusCode || responseContent.Contains("\"error\""))
                {
                    _logger?.LogError($"ArcGIS Update Failed for Needy {objectId}: {responseContent}");
                    return false;
                }
                try
                {
                    using (JsonDocument doc = JsonDocument.Parse(responseContent))
                    {
                        if (doc.RootElement.TryGetProperty("updateResults", out JsonElement results) && results.GetArrayLength() > 0)
                        {
                            return results[0].GetProperty("success").GetBoolean();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger?.LogError(ex, "Failed to parse JSON for UpdateNeedyNeededAmountAsync");
                }
                return false;
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
            var url = $"{_settings.DonorsServiceUrl}/updateFeatures";
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
            var url = $"{_settings.DonationsLayerUrl}/query?where=donor_email='{donorEmail}'&outFields=*&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<ArcGisDonation>>(response);
            return data?.features?.Select(f => f.attributes).ToList() ?? new();
        }

        public async Task<CharityFeature> GetDonorByEmailAsync(string email)
        {
            var url = $"{_settings.DonorsServiceUrl}/query?where=enter_your_e_mail='{email}'&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            var feature = data?.features?.FirstOrDefault();
            if (feature?.attributes != null)
            {
                feature.attributes.x = feature.geometry?.x;
                feature.attributes.y = feature.geometry?.y;
            }
            return feature?.attributes;
        }

        // البحث عن الجمعية الخيرية بالإيميل
        public async Task<CharityFeature> GetCharityByEmailAsync(string email)
        {
            var url = $"{_settings.CharitiesServiceUrl}/query?where=enter_your_e_mail='{email.Replace("'", "''")}'&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            var feature = data?.features?.FirstOrDefault();
            if (feature?.attributes != null)
            {
                feature.attributes.x = feature.geometry?.x;
                feature.attributes.y = feature.geometry?.y;
            }
            return feature?.attributes;
        }

        // البحث عن المحتاج بالإيميل
        public async Task<NeedyFeature> GetNeedyByEmailAsync(string email)
        {
            var url = $"{_settings.NeediesServiceUrl}/query?where=email='{email.Replace("'", "''")}'&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<NeedyFeature>>(response);
            var feature = data?.features?.FirstOrDefault();
            if (feature?.attributes != null)
            {
                feature.attributes.x = feature.geometry?.x;
                feature.attributes.y = feature.geometry?.y;
            }
            return feature?.attributes;
        }

        // Send charity registration data to ArcGIS Feature Layer
        public async Task<bool> SendCharityDataAsync(string charityName, string charitySector, string numberOfCasesSponsoredMonth, string monthlyDonationAmount, double howMuchDoYouNeed, string email, double x, double y)
        {
            var url = $"{_settings.CharitiesServiceUrl}/addFeatures";
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


        public async Task<bool> UpdateNeedyByEmailAsync(string email, double newNeededAmount)
        {
            var needy = await GetNeedyByEmailAsync(email);
            if (needy?.objectid == null) return false;
            return await UpdateNeedyNeededAmountAsync(needy.objectid.Value, newNeededAmount);
        }

        public async Task<bool> UpdateCharityByEmailAsync(string email, double newNeededAmount)
        {
            var charity = await GetCharityByEmailAsync(email);
            if (charity?.objectid == null) return false;
            return await UpdateCharityNeededAmountAsync(charity.objectid.Value, newNeededAmount);
        }

        public async Task<bool> SendCharityDataAsync(RegisterModel model)
        {
            var url = $"{_settings.CharitiesServiceUrl}/addFeatures";
            string charitySectorStr = model.CharitySector != null ? string.Join(",", model.CharitySector) : "";

            var attributes = new Dictionary<string, object>
    {
        { "charity_name", model.CharityName ?? "" },
        { "charity_sector", charitySectorStr },
        { "field_9", model.CasesSponsored ?? "" }, 
        { "field_10", model.MonthlyDonation ?? "" },
        { "how_much_do_you_need", model.CharityNeededAmount ?? 0 },
        { "enter_your_e_mail", model.Email }
    };

            return await AddFeatureAsync(url, attributes, model.Latitude, model.Longitude);
        }

        public async Task<bool> SendDonorDataAsync(RegisterModel model)
        {
            var url = $"{_settings.DonorsServiceUrl}/addFeatures";
            string preferredAidCategoryStr = model.PreferredAidCategory != null ? string.Join(",", model.PreferredAidCategory) : "";

            var attributes = new Dictionary<string, object>
    {
        { "full_name", model.FullName ?? "" },
        { "type_of_donation", model.TypeOfDonation ?? "" },
        { "donation_amount_in_egp", model.DonationAmountInEgp ?? 0 },
        { "preferred_aid_category", preferredAidCategoryStr },
        { "who_would_you_like_to_donate_to", model.WhoWouldYouLikeToDonateTo ?? "" },
        { "enter_your_e_mail", model.Email }
    };

            return await AddFeatureAsync(url, attributes, model.Latitude, model.Longitude);
        }

        // دالة مساعدة عامة لإضافة البيانات لتجنب تكرار الكود
        private async Task<bool> AddFeatureAsync(string serviceUrl, Dictionary<string, object> attributes, double? latitude, double? longitude)
        {
            var feature = new
            {
                attributes,
                geometry = new { x = longitude ?? 0, y = latitude ?? 0, spatialReference = new { wkid = 4326 } }
            };
            var featuresList = new List<object> { feature };
            var featuresJson = JsonSerializer.Serialize(featuresList);
            var content = new FormUrlEncodedContent(new[] {
        new KeyValuePair<string, string>("features", featuresJson),
        new KeyValuePair<string, string>("f", "json")
    });

            try
            {
                var response = await _client.PostAsync(serviceUrl, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode || responseContent.Contains("error"))
                {
                    _logger?.LogError("ArcGIS AddFeature Failed: {response}", responseContent);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Exception in AddFeatureAsync for URL: {url}", serviceUrl);
                return false;
            }
        }
    }

}
