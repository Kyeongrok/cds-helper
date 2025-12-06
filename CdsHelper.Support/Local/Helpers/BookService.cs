using System.IO;
using System.Text.Json;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Support.Local.Helpers;

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

    public List<Book> Filter(
        IEnumerable<Book> books,
        string? nameSearch = null,
        string? librarySearch = null,
        string? hintSearch = null,
        string? language = null,
        string? requiredSkill = null)
    {
        var filtered = books.AsEnumerable();

        // 도서명 검색
        if (!string.IsNullOrWhiteSpace(nameSearch))
        {
            filtered = filtered.Where(b => b.Name.Contains(nameSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 소재 도서관 검색
        if (!string.IsNullOrWhiteSpace(librarySearch))
        {
            filtered = filtered.Where(b => b.Library.Contains(librarySearch, StringComparison.OrdinalIgnoreCase));
        }

        // 게제 힌트 검색
        if (!string.IsNullOrWhiteSpace(hintSearch))
        {
            filtered = filtered.Where(b => b.Hint.Contains(hintSearch, StringComparison.OrdinalIgnoreCase));
        }

        // 언어 필터
        if (!string.IsNullOrWhiteSpace(language))
        {
            filtered = filtered.Where(b => b.Language.Equals(language, StringComparison.OrdinalIgnoreCase));
        }

        // 필요 스킬 필터
        if (!string.IsNullOrWhiteSpace(requiredSkill))
        {
            filtered = filtered.Where(b => b.Required.Equals(requiredSkill, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToList();
    }

    public List<string> GetDistinctLanguages(IEnumerable<Book> books)
    {
        return books
            .Select(b => b.Language)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .OrderBy(l => l)
            .ToList();
    }

    public List<string> GetDistinctRequiredSkills(IEnumerable<Book> books)
    {
        return books
            .Select(b => b.Required)
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Distinct()
            .OrderBy(r => r)
            .ToList();
    }
}
