using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace QuiverLauncher.Services;

public sealed record LanguageOption(string Code, string DisplayName);

/// <summary>
/// Provides the launcher's UI language and the translations shipped with Quiver.
/// English remains the fallback so a missing translation never hides a UI label.
/// </summary>
public static class LocalizationService
{
    public const string SystemLanguage = "system";
    public const string EnglishLanguage = "en";
    public const string PortugueseBrazilLanguage = "pt-BR";

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(SystemLanguage, "System default"),
        new(EnglishLanguage, "English"),
        new(PortugueseBrazilLanguage, "Português (Brasil)"),
    ];

    private static string _language = EnglishLanguage;
    private static string _effectiveLanguage = EnglishLanguage;

    private static readonly object LocalizedTextLock = new();
    private static readonly List<WeakReference<LocalizedText>> LocalizedTexts = [];

    public static string Language => _language;

    public static string EffectiveLanguage => _effectiveLanguage;

    public static bool IsSupported(string? language) =>
        string.Equals(language, SystemLanguage, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, EnglishLanguage, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, PortugueseBrazilLanguage, StringComparison.OrdinalIgnoreCase);

    public static void SetLanguage(string? language)
    {
        var normalized = IsSupported(language) ? Normalize(language!) : SystemLanguage;
        var effective = normalized == SystemLanguage ? DetectSystemLanguage() : normalized;

        if (normalized == _language && effective == _effectiveLanguage)
            return;

        _language = normalized;
        _effectiveLanguage = effective;
        NotifyLocalizedTexts();
    }

    public static string Translate(string source)
    {
        if (_effectiveLanguage != PortugueseBrazilLanguage)
            return source;

        return PortugueseTranslations.TryGetValue(source, out var translation)
            ? translation
            : source;
    }

    private static string Normalize(string language) =>
        language.Equals(PortugueseBrazilLanguage, StringComparison.OrdinalIgnoreCase)
            ? PortugueseBrazilLanguage
            : language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase)
                ? EnglishLanguage
                : SystemLanguage;

    private static string DetectSystemLanguage() =>
        CultureInfo.CurrentUICulture.Name.StartsWith("pt-BR", StringComparison.OrdinalIgnoreCase)
            ? PortugueseBrazilLanguage
            : EnglishLanguage;

    internal static void Register(LocalizedText text)
    {
        lock (LocalizedTextLock)
            LocalizedTexts.Add(new WeakReference<LocalizedText>(text));
    }

    private static void NotifyLocalizedTexts()
    {
        List<LocalizedText> liveTexts;
        lock (LocalizedTextLock)
        {
            liveTexts = [];
            var alive = new List<WeakReference<LocalizedText>>(LocalizedTexts.Count);
            foreach (var reference in LocalizedTexts)
            {
                if (reference.TryGetTarget(out var text))
                {
                    alive.Add(reference);
                    liveTexts.Add(text);
                }
            }
            LocalizedTexts.Clear();
            LocalizedTexts.AddRange(alive);
        }

        foreach (var text in liveTexts)
            text.Refresh();
    }

    // Keep source text as the key. This makes XAML readable and lets untranslated
    // strings fall back to English without requiring a second copy of the UI.
    private static readonly IReadOnlyDictionary<string, string> PortugueseTranslations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Close settings"] = "Fechar configurações",
            ["General"] = "Geral",
            ["Language"] = "Idioma",
            ["Choose the language used by Quiver Launcher."] = "Escolha o idioma usado pelo Quiver Launcher.",
            ["BEHAVIOUR"] = "COMPORTAMENTO",
            ["Close After Launch"] = "Fechar após iniciar",
            ["Close the launcher automatically after starting an app"] = "Fechar o launcher automaticamente após iniciar um aplicativo",
            ["Close to system tray"] = "Fechar para a bandeja do sistema",
            ["Keep Quiver Launcher running in the notification area when the window is closed"] = "Manter o Quiver Launcher em execução na área de notificação quando a janela for fechada",
            ["Ignore The/A/An when sorting by name"] = "Ignorar The/A/An ao ordenar por nome",
            ["Alphabetical sorts skip leading The, A, and An (e.g. The Legend of Zelda under L)"] = "A ordenação alfabética ignora The, A e An no início (por exemplo, The Legend of Zelda fica em L)",
            ["Mouse wheel scroll speed"] = "Velocidade da roda do mouse",
            ["Scroll amount per wheel tick in the library and app catalog."] = "Quantidade deslocada por giro na biblioteca e no catálogo de aplicativos.",
            ["Enable Auto Update for newly added apps"] = "Ativar atualização automática para aplicativos recém-adicionados",
            ["New apps from the catalog or Add Entry start with Auto Update enabled"] = "Novos aplicativos do catálogo ou adicionados manualmente começam com a atualização automática ativada",
            ["Check for app updates in the background"] = "Verificar atualizações de aplicativos em segundo plano",
            ["Scan for updates on a schedule while Quiver Launcher is running (including in the tray)"] = "Procurar atualizações periodicamente enquanto o Quiver Launcher estiver em execução (inclusive na bandeja)",
            ["Background check interval"] = "Intervalo da verificação em segundo plano",
            ["Every 15 minutes"] = "A cada 15 minutos",
            ["Every 30 minutes"] = "A cada 30 minutos",
            ["Every hour"] = "A cada hora",
            ["Every 3 hours"] = "A cada 3 horas",
            ["Every 6 hours"] = "A cada 6 horas",
            ["Every 12 hours"] = "A cada 12 horas",
            ["Every day"] = "Todos os dias",
            ["Prompt when catalog updates are available"] = "Avisar quando houver atualizações do catálogo",
            ["Prompt when library apps have updates"] = "Avisar quando houver atualizações dos aplicativos da biblioteca",
            ["APP INSTALL PATH"] = "CAMINHO DE INSTALAÇÃO DOS APLICATIVOS",
            ["Leave blank to use the default location"] = "Deixe em branco para usar o local padrão",
            ["Reset to Default"] = "Restaurar padrão",
            ["PLATFORM & CACHE"] = "PLATAFORMA E CACHE",
            ["Change Platform"] = "Alterar plataforma",
            ["Clear Icon Cache"] = "Limpar cache de ícones",
            ["Appearance"] = "Aparência",
            ["WINDOW"] = "JANELA",
            ["Start in Fullscreen"] = "Iniciar em tela cheia",
            ["Launch the application in fullscreen mode"] = "Iniciar o aplicativo no modo de tela cheia",
            ["Show OS Top Bar"] = "Mostrar barra superior do sistema",
            ["Display your operating system's title bar"] = "Mostrar a barra de título do sistema operacional",
            ["Interface scale"] = "Escala da interface",
            ["Adjust the size of text and controls in Quiver. Applies immediately."] = "Ajuste o tamanho dos textos e controles do Quiver. Aplicado imediatamente.",
            ["THEME COLORS"] = "CORES DO TEMA",
            ["Primary Color"] = "Cor primária",
            ["Secondary Color"] = "Cor secundária",
            ["BACKGROUND IMAGE"] = "IMAGEM DE FUNDO",
            ["PNG, JPG, and GIF formats supported"] = "Formatos PNG, JPG e GIF compatíveis",
            ["Select Image"] = "Selecionar imagem",
            ["Clear Background"] = "Limpar fundo",
            ["BACKGROUND MUSIC"] = "MÚSICA DE FUNDO",
            ["MP3, WAV, and OGG formats supported"] = "Formatos MP3, WAV e OGG compatíveis",
            ["Select Music"] = "Selecionar música",
            ["Clear Music"] = "Limpar música",
            ["App Cards"] = "Cartões de aplicativos",
            ["LAYOUT"] = "LAYOUT",
            ["Grid View"] = "Visualização em grade",
            ["Display apps in a grid instead of a list"] = "Exibir aplicativos em uma grade em vez de uma lista",
            ["Details in card"] = "Detalhes no cartão",
            ["Presets"] = "Predefinições",
            ["CARD CONTENT"] = "CONTEÚDO DO CARTÃO",
            ["Library name style"] = "Estilo do nome na biblioteca",
            ["Name only"] = "Somente nome",
            ["Name + project below"] = "Nome + projeto abaixo",
            ["Name (project) in title"] = "Nome (projeto) no título",
            ["Project only"] = "Somente projeto",
            ["No limit"] = "Sem limite",
            ["ACTION BUTTONS"] = "BOTÕES DE AÇÃO",
            ["GAME IMAGES"] = "IMAGENS DOS JOGOS",
            ["Fill Cards"] = "Preencher cartões",
            ["Control image transparency"] = "Controlar transparência da imagem",
            ["Advanced"] = "Avançado",
            ["Controls"] = "Controles",
            ["Save token"] = "Salvar token",
            ["Clear token"] = "Limpar token",
            ["Create token"] = "Criar token",
            ["Setup guide"] = "Guia de configuração",
            ["Include preview updates"] = "Incluir atualizações de prévia",
            ["Library"] = "Biblioteca",
            ["App Catalog"] = "Catálogo de aplicativos",
            ["Continue"] = "Continuar",
            ["Search"] = "Pesquisar",
            ["Sort"] = "Ordenar",
            ["Settings"] = "Configurações",
            ["Add New Entry"] = "Adicionar nova entrada",
            ["Back to sources"] = "Voltar às fontes",
            ["Back to Library"] = "Voltar à biblioteca",
            ["Cancel"] = "Cancelar",
            ["Save"] = "Salvar",
            ["Close"] = "Fechar",
            ["Retry"] = "Tentar novamente",
            ["Update"] = "Atualizar",
            ["Update All"] = "Atualizar tudo",
            ["Download"] = "Baixar",
            ["Launch"] = "Iniciar",
            ["Edit"] = "Editar",
            ["Delete"] = "Excluir",
            ["Remove"] = "Remover",
            ["Open"] = "Abrir",
            ["Refresh"] = "Atualizar",
            ["Yes"] = "Sim",
            ["No"] = "Não",
            ["OK"] = "OK",
            ["Installed"] = "Instalado",
            ["Not Installed First"] = "Não instalados primeiro",
            ["Installed First"] = "Instalados primeiro",
            ["Last Played"] = "Jogados recentemente",
            ["Name (A-Z)"] = "Nome (A-Z)",
            ["Name (Z-A)"] = "Nome (Z-A)",
            ["Your library is empty"] = "Sua biblioteca está vazia",
            ["No apps match this search"] = "Nenhum aplicativo corresponde à pesquisa",
            ["Loading your library…"] = "Carregando sua biblioteca…",
            ["About"] = "Sobre",
            ["Add"] = "Adicionar",
            ["Add all"] = "Adicionar tudo",
            ["Add all new"] = "Adicionar todos os novos",
            ["Add App Manually"] = "Adicionar aplicativo manualmente",
            ["Add Custom Image"] = "Adicionar imagem personalizada",
            ["Add Display Filter"] = "Adicionar filtro de exibição",
            ["Add list"] = "Adicionar lista",
            ["Add tag filter"] = "Adicionar filtro de tags",
            ["Add to Steam"] = "Adicionar à Steam",
            ["Adjust the left spacing of text labels"] = "Ajustar o espaçamento à esquerda dos rótulos de texto",
            ["All"] = "Todos",
            ["All Apps"] = "Todos os aplicativos",
            ["All connected controllers are used. No exclusive selection."] = "Todos os controles conectados são usados. Não há seleção exclusiva.",
            ["All platforms"] = "Todas as plataformas",
            ["All tags"] = "Todas as tags",
            ["Any tag"] = "Qualquer tag",
            ["App details"] = "Detalhes do aplicativo",
            ["App options"] = "Opções do aplicativo",
            ["Auto Update"] = "Atualização automática",
            ["Automatic"] = "Automático",
            ["BINDINGS"] = "ATALHOS",
            ["Browse"] = "Procurar",
            ["Browse app lists and choose what to add to your library. Review new apps and changes here."] = "Explore listas de aplicativos e escolha o que adicionar à sua biblioteca. Revise novos aplicativos e alterações aqui.",
            ["Browse Community Catalog"] = "Explorar catálogo da comunidade",
            ["Bulk"] = "Em massa",
            ["Bulk add, merge, and skip"] = "Adicionar, mesclar e ignorar em massa",
            ["Cancel download"] = "Cancelar download",
            ["Card Image Opacity:"] = "Opacidade da imagem do cartão:",
            ["Catalog entries you have not added, including ignored"] = "Entradas do catálogo que você não adicionou, incluindo as ignoradas",
            ["Catalog update available — click for details"] = "Atualização do catálogo disponível — clique para ver os detalhes",
            ["Change Version"] = "Alterar versão",
            ["Changed"] = "Alterado",
            ["Changelog"] = "Registro de alterações",
            ["Check details"] = "Ver detalhes",
            ["Check for updates"] = "Verificar atualizações",
            ["Choose the read_api scope for release access."] = "Escolha o escopo read_api para acessar os lançamentos.",
            ["Clear search"] = "Limpar pesquisa",
            ["Compatibility checks"] = "Verificações de compatibilidade",
            ["CONNECTED CONTROLLERS"] = "CONTROLES CONECTADOS",
            ["Create Desktop Shortcut"] = "Criar atalho na área de trabalho",
            ["Custom Display Name"] = "Nome de exibição personalizado",
            ["Custom Name"] = "Nome personalizado",
            ["Customize"] = "Personalizar",
            ["Details"] = "Detalhes",
            ["Disabled"] = "Desativado",
            ["Discord"] = "Discord",
            ["Dismiss"] = "Dispensar",
            ["Dismiss for a week"] = "Dispensar por uma semana",
            ["Dismiss update status"] = "Dispensar status da atualização",
            ["Don't show this again"] = "Não mostrar novamente",
            ["e.g. mods"] = "ex.: mods",
            ["e.g. n64, recomp"] = "ex.: n64, recomp",
            ["e.g. portable.txt"] = "ex.: portable.txt",
            ["Edit Entry"] = "Editar entrada",
            ["Edit Tags"] = "Editar tags",
            ["ENABLE"] = "ATIVAR",
            ["Enable Gamepad Input"] = "Ativar entrada pelo controle",
            ["Enabled"] = "Ativado",
            ["Every app in this list"] = "Todos os aplicativos desta lista",
            ["Example: flatpak run com.usebottles.bottles -e {exe}"] = "Exemplo: flatpak run com.usebottles.bottles -e {exe}",
            ["Exclude match mode"] = "Modo de correspondência da exclusão",
            ["Exclude tags"] = "Excluir tags",
            ["Files To Add"] = "Arquivos para adicionar",
            ["Filter apps by platforms in the latest release"] = "Filtrar aplicativos por plataforma no lançamento mais recente",
            ["Filter by tag: off → include → exclude"] = "Filtrar por tag: desativado → incluir → excluir",
            ["Filter name"] = "Nome do filtro",
            ["Folder Name"] = "Nome da pasta",
            ["Force Update"] = "Forçar atualização",
            ["Gamepad:"] = "Controle:",
            ["Get early Quiver Launcher releases. Preview versions may be unstable."] = "Receba versões antecipadas do Quiver Launcher. Versões de prévia podem ser instáveis.",
            ["Get started by adding an app yourself, or browse the Quiver Community App Catalog to add apps from the community."] = "Comece adicionando um aplicativo ou explore o catálogo de aplicativos da comunidade do Quiver para adicionar aplicativos criados pela comunidade.",
            ["GitHub rate-limits unauthenticated use, so catalog refresh and downloads can fail or stall."] = "O GitHub limita requisições sem autenticação, portanto a atualização do catálogo e os downloads podem falhar ou travar.",
            ["Global override for Windows-only apps on Linux when an app uses Auto. Use {exe}, {gamePath}, or {exeDir}. Per-app runner/prefix is under Launch Options → Windows Runner. Leave blank to auto-detect Proton then Wine."] = "Substituição global para aplicativos exclusivos do Windows no Linux quando um aplicativo usa Automático. Use {exe}, {gamePath} ou {exeDir}. O executor/prefixo por aplicativo fica em Opções de inicialização → Executor do Windows. Deixe em branco para detectar Proton e depois Wine automaticamente.",
            ["Grid"] = "Grade",
            ["Hidden"] = "Ocultos",
            ["Hidden from review until you unhide them"] = "Ocultos da revisão até que você os reexiba",
            ["Hide"] = "Ocultar",
            ["Hide App"] = "Ocultar aplicativo",
            ["Hide sidebar"] = "Ocultar barra lateral",
            ["Hide this app from this catalog list. It will not appear in Needs Review or count as a catalog update until you Unhide it. Find it again under Hidden."] = "Ocultar este aplicativo desta lista do catálogo. Ele não aparecerá em Precisa de revisão nem contará como atualização do catálogo até ser reexibido. Encontre-o novamente em Ocultos.",
            ["How many wrapped lines of tags each library card can show. 0 hides tags on cards; filters still work."] = "Quantas linhas quebradas de tags cada cartão da biblioteca pode mostrar. 0 oculta as tags nos cartões; os filtros continuam funcionando.",
            ["How names and projects appear in the Library. “Below” shows the project name under the title. Custom display names always win when set."] = "Como nomes e projetos aparecem na biblioteca. “Abaixo” mostra o nome do projeto sob o título. Nomes personalizados sempre têm prioridade quando definidos.",
            ["Icon URL"] = "URL do ícone",
            ["Idle cards show an ellipsis. Hover or gamepad/keyboard focus slides the full name, project, and version text."] = "Cartões inativos mostram reticências. Passe o mouse ou use o foco do controle/teclado para deslizar o nome, projeto e versão completos.",
            ["If this repo ships more than one game, text that appears only in this game’s filenames (e.g. EXIT1)"] = "Se este repositório distribuir mais de um jogo, texto que aparece apenas nos nomes de arquivo deste jogo (ex.: EXIT1)",
            ["Ignore"] = "Ignorar",
            ["In your library and matching this list"] = "Na sua biblioteca e correspondentes a esta lista",
            ["In your library, but catalog name, tags, or other fields differ"] = "Na sua biblioteca, mas com nome, tags ou outros campos diferentes do catálogo",
            ["Include match mode"] = "Modo de correspondência da inclusão",
            ["Include tags"] = "Incluir tags",
            ["Increases API rate limits for faster and more reliable downloads"] = "Aumenta os limites da API para downloads mais rápidos e confiáveis",
            ["Install each mod into its own folder"] = "Instalar cada mod em sua própria pasta",
            ["Installed Only"] = "Somente instalados",
            ["Keep name, status, and buttons inside each grid card"] = "Manter nome, status e botões dentro de cada cartão da grade",
            ["Keyboard:"] = "Teclado:",
            ["Landscape"] = "Paisagem",
            ["Last updated"] = "Última atualização",
            ["Latest: "] = "Mais recente: ",
            ["Launch Options"] = "Opções de inicialização",
            ["Left Padding:"] = "Espaçamento esquerdo:",
            ["Linux ARM64"] = "Linux ARM64",
            ["LINUX WINDOWS RUNNER"] = "EXECUTOR WINDOWS NO LINUX",
            ["Linux X64"] = "Linux X64",
            ["List"] = "Lista",
            ["Loading README…"] = "Carregando README…",
            ["Locate Existing Install"] = "Localizar instalação existente",
            ["Mac"] = "Mac",
            ["MacOS"] = "macOS",
            ["Manually managed (no repository or updates)"] = "Gerenciado manualmente (sem repositório ou atualizações)",
            ["Menu"] = "Menu",
            ["Merge"] = "Mesclar",
            ["Merge all changed"] = "Mesclar todas as alterações",
            ["Mod Sources"] = "Fontes de mods",
            ["Mod updates available"] = "Atualizações de mods disponíveis",
            ["Mods"] = "Mods",
            ["Mods Path"] = "Caminho dos mods",
            ["More"] = "Mais",
            ["Most downloaded"] = "Mais baixados",
            ["Move Down"] = "Mover para baixo",
            ["Move Up"] = "Mover para cima",
            ["Name"] = "Nome",
            ["Navigate with the bound controls. The selected app is highlighted."] = "Navegue com os controles configurados. O aplicativo selecionado fica destacado.",
            ["Needs review"] = "Precisa de revisão",
            ["New and changed apps that still need action"] = "Aplicativos novos e alterados que ainda precisam de uma ação",
            ["Newest"] = "Mais recentes",
            ["No app updates pending."] = "Nenhuma atualização de aplicativo pendente.",
            ["No apps match this filter."] = "Nenhum aplicativo corresponde a este filtro.",
            ["No mods found."] = "Nenhum mod encontrado.",
            ["Not in library"] = "Fora da biblioteca",
            ["Off → include → exclude"] = "Desativado → incluir → excluir",
            ["Open Folder"] = "Abrir pasta",
            ["Open Mod Folder"] = "Abrir pasta do mod",
            ["Open Page"] = "Abrir página",
            ["Open Repo"] = "Abrir repositório",
            ["Open Repository"] = "Abrir repositório",
            ["Optional. Raises GitLab API rate limits and allows private gitlab.com projects"] = "Opcional. Aumenta os limites da API do GitLab e permite projetos privados do gitlab.com",
            ["Overrides library name style when set"] = "Substitui o estilo de nome da biblioteca quando definido",
            ["Portrait"] = "Retrato",
            ["Preferred: "] = "Preferido: ",
            ["Project"] = "Projeto",
            ["Quiver updates"] = "Atualizações do Quiver",
            ["README"] = "README",
            ["Refresh Controllers"] = "Atualizar controles",
            ["Refresh lists"] = "Atualizar listas",
            ["Release asset filter"] = "Filtro de recursos do lançamento",
            ["Release notes"] = "Notas da versão",
            ["Remove Custom Image"] = "Remover imagem personalizada",
            ["Remove from Library"] = "Remover da biblioteca",
            ["Remove from List"] = "Remover da lista",
            ["Replace"] = "Substituir",
            ["Repository"] = "Repositório",
            ["Repository (A-Z)"] = "Repositório (A-Z)",
            ["Repository Source"] = "Fonte do repositório",
            ["Reset Bindings to Defaults"] = "Restaurar atalhos padrão",
            ["Review Catalog Changes"] = "Revisar alterações do catálogo",
            ["Save to apply your token and resume waiting checks."] = "Salve para aplicar seu token e retomar as verificações pendentes.",
            ["Search mods"] = "Pesquisar mods",
            ["Search this list"] = "Pesquisar nesta lista",
            ["Select Different Executable"] = "Selecionar outro executável",
            ["Set a GitHub token"] = "Definir um token do GitHub",
            ["Show"] = "Mostrar",
            ["Show a popup when catalog sources have changes to review (off by default; open App Catalog anytime)"] = "Mostrar um aviso quando as fontes do catálogo tiverem alterações para revisar (desativado por padrão; abra o Catálogo de aplicativos quando quiser)",
            ["Show all pending reviews"] = "Mostrar todas as revisões pendentes",
            ["Show app update prompts automatically. Manual update checks still show available updates, and Quiver Launcher self-update prompts still appear."] = "Mostrar avisos de atualização de aplicativos automaticamente. Verificações manuais continuam mostrando atualizações disponíveis, e os avisos de atualização do próprio Quiver Launcher continuam aparecendo.",
            ["Show catalog update badges on library cards"] = "Mostrar indicadores de atualização do catálogo nos cartões da biblioteca",
            ["Show Changelog"] = "Mostrar registro de alterações",
            ["Size of Play, Download, and Update buttons"] = "Tamanho dos botões Iniciar, Baixar e Atualizar",
            ["Size:"] = "Tamanho:",
            ["Skip"] = "Ignorar",
            ["Skip all"] = "Ignorar tudo",
            ["Skip Update"] = "Ignorar atualização",
            ["Small badge when catalog metadata for an app has changes to review (app version updates use the orange update button)"] = "Indicador pequeno quando os metadados de um aplicativo no catálogo têm alterações para revisar (atualizações de versão usam o botão laranja)",
            ["Sort:"] = "Ordenar:",
            ["Square"] = "Quadrado",
            ["Square Alt"] = "Quadrado alternativo",
            ["Status"] = "Status",
            ["TAG FILTERS"] = "FILTROS DE TAG",
            ["Tag lines on library cards"] = "Linhas de tags nos cartões da biblioteca",
            ["Tags"] = "Tags",
            ["Tags (comma-separated, e.g. n64, favorites)"] = "Tags (separadas por vírgulas, ex.: n64, favoritos)",
            ["Tags to hide (comma-separated, optional, e.g. ai)"] = "Tags a ocultar (separadas por vírgulas, opcional, ex.: ai)",
            ["Team / project / author (optional)"] = "Equipe / projeto / autor (opcional)",
            ["TEXT"] = "TEXTO",
            ["Text Padding:"] = "Espaçamento do texto:",
            ["Thunderstore URL/slug or GameBanana mods/games/{id} (one per line)"] = "URL/slug do Thunderstore ou mods/games/{id} do GameBanana (um por linha)",
            ["Top rated"] = "Mais bem avaliados",
            ["Truncate name, project, and versions on library cards"] = "Truncar nome, projeto e versões nos cartões da biblioteca",
            ["Try a different search, or clear it to show your library again."] = "Tente outra pesquisa ou limpe-a para mostrar sua biblioteca novamente.",
            ["Unhide"] = "Reexibir",
            ["Unhide App"] = "Reexibir aplicativo",
            ["Uninstall"] = "Desinstalar",
            ["+ Add"] = "+ Adicionar",
            ["← Back to sources"] = "← Voltar às fontes",
            ["0 lines"] = "0 linhas",
            ["1 line"] = "1 linha",
            ["2 lines"] = "2 linhas",
            ["3 lines"] = "3 linhas",
            ["4 lines"] = "4 linhas",
            ["Android"] = "Android",
            ["GitHub"] = "GitHub",
            ["GITHUB API TOKEN"] = "TOKEN DA API DO GITHUB",
            ["GitLab"] = "GitLab",
            ["GITLAB API TOKEN"] = "TOKEN DA API DO GITLAB",
            ["Ko-fi"] = "Ko-fi",
            ["LAUNCHER"] = "LAUNCHER",
            ["Linux"] = "Linux",
            ["Opacity:"] = "Opacidade:",
            ["QUIVER"] = "QUIVER",
            ["Up to date"] = "Atualizado",
            ["Update Now"] = "Atualizar agora",
            ["Updates first"] = "Atualizações primeiro",
            ["Versions"] = "Versões",
            ["View and platform filters"] = "Filtros de visualização e plataforma",
            ["View README"] = "Ver README",
            ["Volume:"] = "Volume:",
            ["Windows"] = "Windows",
            ["Windows Runner…"] = "Executor do Windows…",
        };
}

public sealed class LocalizedText : INotifyPropertyChanged
{
    public LocalizedText(string source)
    {
        Source = source;
        LocalizationService.Register(this);
    }

    public string Source { get; }

    public string Value => LocalizationService.Translate(Source);

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
}

public sealed class TranslateExtension : MarkupExtension
{
    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocalizedText.Value))
        {
            Source = new LocalizedText(Text),
        };
}
