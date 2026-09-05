using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace AgenteEmprego.Services
{
    public class Gmail_Service
    {
        private static readonly string[] Scopes = { GmailService.Scope.GmailReadonly };
        private const string ApplicationName = "Agente_Emprego_Worker";

        // Lazy Loading: Autentica apenas uma vez por execução do Worker
        private readonly Lazy<Task<GmailService>> _serviceLazy;

        public Gmail_Service()
        {
            _serviceLazy = new Lazy<Task<GmailService>>(Autenticar_Async);
        }

        private async Task<GmailService> Autenticar_Async()
        {
            await using var stream = new FileStream("Gmail_Client_Secret.json", FileMode.Open, FileAccess.Read);

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                GoogleClientSecrets.FromStream(stream).Secrets,
                Scopes,
                "user",
                CancellationToken.None,
                new FileDataStore("Token_Gmail", true));

            return new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = ApplicationName,
            });
        }

        /// <summary>
        /// Busca e-mails não lidos a partir de uma data inicial. A data deve corresponder à última
        /// execução registrada em Log_Rodagens (Notion), evitando reprocessamento e falhas de janela fixa.
        /// </summary>
        /// <param name="dataInicial">Data/hora a partir da qual os e-mails serão considerados (exclusive, granularidade de segundo via epoch Gmail).</param>
        public async Task<List<Message>> Buscar_Emails_Nao_Lidos_Async(DateTime dataInicial)
        {
            var service = await _serviceLazy.Value;
            var request = service.Users.Messages.List("me");

            long epochSeconds = new DateTimeOffset(dataInicial).ToUnixTimeSeconds();
            request.Q = $"is:unread after:{epochSeconds}";

            var response = await request.ExecuteAsync();
            return response.Messages?.ToList() ?? new List<Message>();
        }

        public async Task<string> Obter_Conteudo_Email_Async(string idMensagem)
        {
            var service = await _serviceLazy.Value;
            var mensagem = await service.Users.Messages.Get("me", idMensagem).ExecuteAsync();
            return mensagem.Snippet ?? string.Empty;
        }
    }
}