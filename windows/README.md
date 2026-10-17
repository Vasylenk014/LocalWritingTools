# WritingTools for Windows

Ця папка призначена для вихідного коду версії WritingTools під Windows (C# .NET WPF або Python PySide6).

## Як додати сюди файли з Windows-комп'ютера:

1. Схилуйте цей репозиторій на вашому Windows-ПК:
   ```bash
   git clone <URL_ВАШОГО_GITHUB_РЕПОЗИТОРІЮ>
   cd WritingTools
   ```
2. Скопіюйте всі файли та папки проекту з Windows у цю папку (`windows/`).
3. Закомітьте та відправте зміни:
   ```bash
   git add windows/
   git commit -m "Add Windows implementation"
   git push
   ```

Детальний опис архітектури та технічна специфікація Windows-версії знаходяться у файлі [docs/WINDOWS_PORT_SPEC.md](../docs/WINDOWS_PORT_SPEC.md).
