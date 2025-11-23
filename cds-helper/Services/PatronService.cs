using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using cds_helper.Models;

namespace cds_helper.Services;

public class PatronService
{
    public List<Patron> LoadPatrons(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"patrons.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var patrons = JsonSerializer.Deserialize<List<Patron>>(json);

        return patrons ?? new List<Patron>();
    }
}
