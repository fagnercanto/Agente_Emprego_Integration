using System;
using System.Threading.Tasks;
using Google.GenAI; // Ajuste para o namespace correto do seu SDK do Gemini

namespace AgenteEmprego.Services
{
    public class Gemini_Service
    {
        private readonly Client _client;

        public Gemini_Service()
        {
            var apiKey = Environment.GetEnvironmentVariable("Gemini_Api_Key")
                ?? throw new InvalidOperationException("Gemini_Api_Key não configurada no local.settings.json.");

            // Injeta a chave direto no cliente. Protege a thread e evita recriar o objeto.
            _client = new Client(apiKey: apiKey);
        }

        public async Task<string> Analisar_Email_Async(string promptSistema, string textoEmail)
        {
            var conteudoFinal = $"{promptSistema}\n\nE-mail:\n{textoEmail}";

            var response = await _client.Models.GenerateContentAsync(
                model: "gemini-3.6-flash", // Ou 3.7-flash, confirme a string no SDK
                contents: conteudoFinal);

            var textoRetorno = response?.Candidates?.Count > 0
                ? response.Candidates[0].Content?.Parts?[0]?.Text
                : null;

            if (string.IsNullOrWhiteSpace(textoRetorno)) return string.Empty;

            return textoRetorno.Replace("```json", "").Replace("```", "").Trim();
        }
    }
}