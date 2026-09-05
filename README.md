# Agente Emprego

Motor de automação e ingestão de dados desenvolvido em C# (.NET) para monitoramento, classificação e registro de vagas de emprego. O sistema atua como um microserviço serverless (Azure Functions) que lê a caixa de entrada, delega a triagem semântica para Inteligência Artificial e persiste os resultados estruturados.

## 🔌 Integrações: como o projeto se comunica com cada serviço

Este projeto orquestra três APIs externas, cada uma com uma responsabilidade única dentro do pipeline:

| Serviço | Papel no projeto | Como é acessado |
|---|---|---|
| **Gmail API** | Fonte de dados de entrada. Fornece os e-mails não lidos que serão triados. | SDK oficial `Google.Apis.Gmail.v1`, autenticado via OAuth2 (`GoogleWebAuthorizationBroker`), consultado com filtros de busca nativos do Gmail (`is:unread`, `after:{epoch}`). |
| **Google Gemini API** | Motor de Inteligência Artificial. Recebe o corpo do e-mail + um prompt de triagem e devolve um veredito estruturado em JSON. | SDK `Google.GenAI`, autenticado por API Key, chamado via `GenerateContentAsync` (modelo `gemini-3.6-flash`). |
| **Notion API** | Banco de dados de negócio (substitui um banco relacional tradicional). Armazena os prompts de triagem, o histórico de execuções (`Log_Rodagens`) e as vagas classificadas (`Quadro_De_Vagas`). | Chamadas HTTP REST diretas via `HttpClient` (`api.notion.com/v1`), autenticadas por Bearer Token, sem SDK oficial. |

Essa separação de responsabilidades permite trocar qualquer uma das três peças (por exemplo, migrar do Gemini para outro LLM) sem impactar as demais integrações.

## 🚀 Arquitetura e Fluxo de Dados

1. **Trigger:** Timer Trigger (Azure Functions) aciona o Worker.
2. **Ingestão (Gmail API):** Conecta à caixa de entrada e extrai e-mails não lidos via query `is:unread`, combinada com o filtro `after:{epoch}`. A data inicial **não é fixa**: é obtida dinamicamente a partir do campo `Data_Execucao` do registro mais recente do banco `Log_Rodagens` no Notion (ordenado de forma decrescente), garantindo que apenas e-mails recebidos após a última execução bem-sucedida sejam processados. Caso não exista nenhuma rodagem anterior, aplica-se uma janela padrão de 3 dias (fallback).
3. **Processamento (Gemini API):** Antes de analisar os e-mails, o Worker consulta o banco `Log_Prompts` no Notion e recupera todos os "motores" de triagem ativos (ver seção **Motores de Prompt** abaixo). Cada e-mail não lido é submetido, um a um, contra **todos** os prompts ativos retornados pelo Notion — não existe prompt fixo no código. O primeiro prompt que identificar `acao_necessaria: true` ou `match: true` interrompe a iteração para aquele e-mail. A IA (`gemini-3.6-flash`) avalia a vaga contra o perfil técnico descrito no prompt e gera um payload estruturado e higienizado (sem marcações Markdown).
4. **Persistência (Notion API):** O Worker desserializa o JSON e cria um novo card no banco de dados `Quadro_De_Vagas` no Notion.
5. **Encerramento:** O e-mail original é marcado como lido no Gmail para evitar reprocessamento (Stateless).

## 🧠 Motores de Prompt (Log_Prompts)

Os critérios de triagem semântica **não são hardcoded** no código-fonte — são gerenciados dinamicamente através de um banco de dados no Notion (`Notion_DbId_Prompts` / `Log_Prompts`), permitindo ajustar ou desativar regras de negócio sem necessidade de deploy.

*   **Consulta:** `NotionRepositoryService.ObterPromptsAtivosAsync()` filtra os registros pela propriedade `Flag_Ativo = true`.
*   **Conteúdo do prompt:** extraído da propriedade `rich_text` chamada `Conteudo_Prompt` de cada card ativo.
*   **Múltiplos motores:** se houver mais de um prompt ativo, cada e-mail é avaliado contra todos eles em sequência, até que um retorne `acao_necessaria: true` ou `match: true`.
*   **Sem prompt ativo:** se `Log_Prompts` não retornar nenhum registro ativo, o ciclo de ingestão é abortado (`LogWarning`) e nenhum e-mail é processado naquela execução.

## 📌 Filosofia de Desenvolvimento: Mínimo Entregável

Este projeto é conduzido pela diretriz de **Mínimo Entregável** (Minimum Viable Deliverable): a evolução é sempre incremental, partindo de uma PoC documentada em folha de papel com no máximo 5 linhas, e avançando arquiteturalmente apenas quando um novo requisito real o exige.

Na prática, isso significa:

*   **Sempre existe um executável funcional.** Nenhuma etapa de evolução deixa o sistema quebrado ou incompleto — cada commit representa um estado operante.
*   **Sem over-engineering antecipado.** Camadas de abstração, padrões de design e generalizações só são introduzidos quando o problema real os justifica (ex.: `NotionRepositoryService` só nasceu quando surgiu a necessidade de múltiplos bancos Notion, não antes).
*   **Riscos aparecem cedo, não tarde.** Ao evitar planejamento excessivo de arquitetura "para o futuro", problemas reais de integração (rate limit, autenticação, formato de resposta da IA) são descobertos e corrigidos nas primeiras iterações, quando o custo de mudança ainda é baixo.
*   **Refatoração é consequência, não pré-requisito.** O código é reestruturado quando a necessidade de negócio muda (por exemplo, a introdução da busca de e-mails por data dinâmica via `Log_Rodagens`), e não antecipadamente.

## 🛠️ Stack Tecnológica

*   **Linguagem:** C# / .NET 10
*   **Infraestrutura:** Azure Functions (Worker Process)
*   **Integrações:**
    *   Gmail API (Leitura de caixa de entrada)
    *   Google Gemini SDK (Triagem semântica)
    *   Notion API (Banco de dados de vagas)

## ⚙️ Configuração do Ambiente

Variáveis de ambiente exigidas no arquivo `local.settings.json`:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "GMAIL_CREDENTIALS_PATH": "Caminho para credentials.json",
    "GEMINI_API_KEY": "Sua chave do Google AI Studio",
    "NOTION_API_KEY": "Sua chave de integração do Notion",
    "NOTION_DATABASE_ID": "ID do Quadro_De_Vagas"
  }
}