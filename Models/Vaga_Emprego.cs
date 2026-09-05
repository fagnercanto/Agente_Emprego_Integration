using System.Text.Json.Serialization;

namespace AgenteEmprego.Models
{
    public class Vaga_Emprego
    {
        [JsonPropertyName("Categoria")]
        public string? Categoria { get; set; }

        [JsonPropertyName("Titulo")]
        public string? Titulo { get; set; }

        [JsonPropertyName("Empresa")]
        public string? Empresa { get; set; }

        [JsonIgnore]
        public bool Alta_Prioridade => Categoria is "TIPO_1" or "TIPO_2"; // Removido o espaço "TIPO 1"
    }
}