using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AgenteEmprego.Services;

namespace AgenteEmprego
{
    public class Agente_Worker_Function
    {
        private readonly ILogger _logger;
        private readonly NotionRepositoryService _notionService;
        private readonly Gmail_Service _gmailService;
        private readonly Gemini_Service _geminiService;

        // Cache estático obrigatório para zero-allocation no parsing
        private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

        public Agente_Worker_Function(
            ILoggerFactory loggerFactory,
            NotionRepositoryService notionService,
            Gmail_Service gmailService,
            Gemini_Service geminiService)
        {
            _logger = loggerFactory.CreateLogger<Agente_Worker_Function>();
            _notionService = notionService;
            _gmailService = gmailService;
            _geminiService = geminiService;
        }

        [Function("Motor_Ingestao")]
        //public async Task Run([TimerTrigger("0 */30 * * * *", RunOnStartup = true)] TimerInfo myTimer)
        public async Task Run([TimerTrigger("0 */30 * * * *")] TimerInfo myTimer)
        {
            _logger.LogInformation("Iniciando ciclo de ingestão de e-mails.");

            var promptsAtivos = await _notionService.ObterPromptsAtivosAsync();
            if (promptsAtivos.Count == 0)
            {
                _logger.LogWarning("Nenhum prompt ativo encontrado no Notion. Abortando execução.");
                return;
            }

            // Regra de negócio: a busca de e-mails deve partir da data da última rodagem registrada
            // em Log_Rodagens, evitando reprocessamento e janelas fixas arbitrárias (ex: "últimos 3 dias").
            DateTime? ultimaExecucao = await _notionService.ObterUltimaDataExecucaoAsync();
            DateTime dataInicialBusca = ultimaExecucao ?? DateTime.Now.AddDays(-3);

            if (ultimaExecucao is null)
            {
                _logger.LogWarning("Nenhuma rodagem anterior encontrada em Log_Rodagens. Utilizando janela padrão de 3 dias.");
            }

            string idRodagem = $"Exec-{DateTime.Now:yyyyMMdd-HHmm}";
            string fkRodagem = await _notionService.CriarRodagemAsync(idRodagem, DateTime.Now);

            var emails = await _gmailService.Buscar_Emails_Nao_Lidos_Async(dataInicialBusca);
            _logger.LogInformation("Foram encontrados {TotalEmails} e-mails não lidos desde {DataInicial}.", emails.Count, dataInicialBusca);

            foreach (var email in emails)
            {
                // Variáveis restauradas no escopo correto do laço externo
                bool acaoIdentificada = false;
                string corpoEmail = await _gmailService.Obter_Conteudo_Email_Async(email.Id);

                foreach (var prompt in promptsAtivos)
                {
                    string respostaTexto = string.Empty;

                    // 1. Tratamento de erro na requisição HTTP da IA
                    try
                    {
                        respostaTexto = await _geminiService.Analisar_Email_Async(prompt, corpoEmail);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Servidor do Gemini indisponível (High Demand) ao processar o e-mail {EmailId}. Tentando o próximo...", email.Id);
                        await Task.Delay(2000); // Backoff de 2 segundos
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(respostaTexto)) continue;

                    // 2. Tratamento de erro no Parsing do JSON
                    try
                    {
                        using var documento = JsonDocument.Parse(respostaTexto);
                        var root = documento.RootElement;

                        if ((root.TryGetProperty("acao_necessaria", out var acao) && acao.GetBoolean()) ||
                            (root.TryGetProperty("match", out var match) && match.GetBoolean()))
                        {
                            acaoIdentificada = true;
                            break;
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Falha de parsing no JSON devolvido pelo Gemini para o e-mail {EmailId}. Payload ignorado.", email.Id);
                        continue;
                    }
                }

                // Inserção no banco de dados do Notion
                await _notionService.RegistrarEmailProcessadoAsync(email.Id, "Assunto Extraído", "Remetente Extraído", acaoIdentificada, fkRodagem);
            }

            _logger.LogInformation("Ciclo de ingestão finalizado. ID Rodagem: {RodagemId}", idRodagem);
        }
    }
}