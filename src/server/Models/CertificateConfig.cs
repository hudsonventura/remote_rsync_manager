using System.ComponentModel.DataAnnotations.Schema;

namespace server.Models;

[Table("certificate_config")]
public class CertificateConfig
{
    public int id { get; set; }
    public string certificatePath { get; set; } = string.Empty;
    public string certificatePassword { get; set; } = string.Empty;
    public DateTime created_at { get; set; }
    public DateTime updated_at { get; set; }
}

