using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using cds_helper.Models;

namespace cds_helper.Services;

public class BookService
{
    public List<Book> LoadBooks(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"books.json 파일을 찾을 수 없습니다: {filePath}");
        }

        var json = File.ReadAllText(filePath);
        var books = JsonSerializer.Deserialize<List<Book>>(json);

        return books ?? new List<Book>();
    }
}
