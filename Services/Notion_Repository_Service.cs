using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AgenteEmprego.Services
{
    public class NotionRepositoryService
    {
        private readonly HttpClient _httpClient;
        private readonly string _notionApiKey;
        private readonly string _promptsDbId;
        private readonly string _rodagensDbId;
        private readonly string _emailsDbId;

        public NotionRepositoryService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _notionApiKey = Environment.GetEnvironmentVariable("Notion_Api_Key");
            _promptsDbId = Environment.GetEnvironmentVariable("Notion_DbId_Prompts");
            _rodagensDbId = Environment.GetEnvironmentVariable("Notion_DbId_Rodagens");
            _emailsDbId = Environment.GetEnvironmentVariable("Notion_DbId_Emails");

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _notionApiKey);
            _httpClient.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
        }

        public async Task<List<string>> ObterPromptsAtivosAsync()
        {
            var url = $"https://api.notion.com/v1/databases/{_promptsDbId}/query";
            var payload = new { filter = new { property = "Flag_Ativo", checkbox = new { equals = true } } };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode) return new List<string>();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);
            var activePrompts = new List<string>();

            foreach (var result in document.RootElement.GetProperty("results").EnumerateArray())
            {
                var properties = result.GetProperty("properties");
                if (properties.TryGetProperty("Conteudo_Prompt", out var promptProp))
                {
                    var textArray = promptProp.GetProperty("rich_text");
                    if (textArray.GetArrayLength() > 0)
                    {
                        var promptText = textArray[0].GetProperty("plain_text").GetString();
                        if (!string.IsNullOrWhiteSpace(promptText))
                        {
                            activePrompts.Add(promptText);
                        }
                    }
                }
            }
            return activePrompts;
        }

        /// <summary>
        /// Consulta o banco Log_Rodagens no Notion e retorna a data/hora da última execução registrada,
        /// ordenando por Data_Execucao decrescente. Retorna null se não houver nenhuma rodagem anterior.
        /// </summary>
        public async Task<DateTime?> ObterUltimaDataExecucaoAsync()
        {
            var url = $"https://api.notion.com/v1/databases/{_rodagensDbId}/query";
            var payload = new
            {
                sorts = new[]
                {
                    new { property = "Data_Execucao", direction = "descending" }
                },
                page_size = 1
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode) return null;

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);

            var results = document.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0) return null;

            var propriedades = results[0].GetProperty("properties");
            if (!propriedades.TryGetProperty("Data_Execucao", out var dataProp)) return null;
            if (!dataProp.TryGetProperty("date", out var dateProp) || dateProp.ValueKind == JsonValueKind.Null) return null;
            if (!dateProp.TryGetProperty("start", out var startProp)) return null;

            var startText = startProp.GetString();
            if (string.IsNullOrWhiteSpace(startText)) return null;

            return DateTimeOffset.Parse(startText).LocalDateTime;
        }

        public async Task<string> CriarRodagemAsync(string idRodagem, DateTime dataExecucao)
        {
            var url = "https://api.notion.com/v1/pages";
            var payload = new
            {
                parent = new { database_id = _rodagensDbId },
                properties = new
                {
                    ID_Rodagem = new { title = new[] { new { text = new { content = idRodagem } } } },
                    Data_Execucao = new { date = new { start = dataExecucao.ToString("yyyy-MM-ddTHH:mm:sszzz") } },
                    Total_Emails_Lidos = new { number = 0 }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(responseJson);
            return document.RootElement.GetProperty("id").GetString();
        }

        public async Task RegistrarEmailProcessadoAsync(string idEmail, string assunto, string remetente, bool acaoNecessaria, string idPaginaRodagem)
        {
            var url = "https://api.notion.com/v1/pages";
            var payload = new
            {
                parent = new { database_id = _emailsDbId },
                properties = new
                {
                    ID_Email = new { title = new[] { new { text = new { content = idEmail } } } },
                    Assunto = new { rich_text = new[] { new { text = new { content = assunto } } } },
                    Remetente = new { rich_text = new[] { new { text = new { content = remetente } } } },
                    Acao_Necessaria = new { checkbox = acaoNecessaria },
                    ID_Rodagem = new { relation = new[] { new { id = idPaginaRodagem } } }
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _httpClient.PostAsync(url, content);
        }
    }
}