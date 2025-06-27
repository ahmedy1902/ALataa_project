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
        public double? x { get; set; } // Longitude
        public double? y { get; set; } // Latitude
    }
    public class NeedyFeature
    {
        public int? objectid { get; set; }
        public string full_name { get; set; }
        public string type_of_need { get; set; }
        public double? how_much_do_you_need { get; set; }
        public double? x { get; set; } // Longitude
        public double? y { get; set; } // Latitude
    }
    public class DonationFeature
    {
        public string donor_email { get; set; }
        public string recipient_email { get; set; }
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
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea_results/FeatureServer/0/query?where=1=1&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<CharityFeature>>(response);
            return data?.features?.Select(f => {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).ToList() ?? new();
        }

        public async Task<List<NeedyFeature>> GetNeediesAsync()
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_1b6326b33d2b4213bf757d6780a0f12a_results/FeatureServer/0/query?where=1=1&outFields=*&returnGeometry=true&f=json";
            var response = await _client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<ArcGisResponse<NeedyFeature>>(response);
            return data?.features?.Select(f => {
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

        public async Task<bool> UpdateDonorNeededAmountAsync(string email, double newNeededAmount)
        {
            //  ÕœÌÀ needed amount ›Ì ·«Ì— «·„ »—⁄Ì‰
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_fb464f56faae4b6c803825277c69be1c_results/FeatureServer/0/updateFeatures";
            // Ã·» objectid ··„ »—⁄
            var donor = await GetDonorByEmailAsync(email);
            if (donor == null || donor.objectid == null)
                return false;
            var features = new[]
            {
                new
                {
                    attributes = new {
                        objectid = donor.objectid,
                        how_much_do_you_need = newNeededAmount
                    }
                }
            };
            var data = new { features, f = "json" };
            var json = JsonSerializer.Serialize(data);
            var response = await _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));
            var responseContent = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode && responseContent.Contains("success");
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
            return data?.features?.Select(f => {
                f.attributes.x = f.geometry?.x;
                f.attributes.y = f.geometry?.y;
                return f.attributes;
            }).FirstOrDefault();
        }

        // Send charity registration data to ArcGIS Feature Layer
        public async Task<bool> SendCharityDataAsync(string charityName, string charitySector, string numberOfCasesSponsoredMonth, string monthlyDonationAmount, double howMuchDoYouNeed, string email, double x, double y)
        {
            var url = "https://services.arcgis.com/LxyOyIfeECQuFOsk/arcgis/rest/services/survey123_2c36d5ade9064fe685d54893df3b37ea/FeatureServer/0/addFeatures";
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
                            enter_your_e_mail = email
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
                return await SendCharityDataAsync(
                    model.CharityName ?? string.Empty,
                    model.CharitySector ?? string.Empty,
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
    }
}
