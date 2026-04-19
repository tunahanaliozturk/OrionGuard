using System.Collections.Concurrent;
using System.Globalization;

namespace Moongazing.OrionGuard.Localization;

/// <summary>
/// Provides thread-safe localization support for validation messages.
/// </summary>
public static class ValidationMessages
{
    private static volatile Func<string, string?, string> _messageResolver = DefaultMessageResolver;
    private static readonly AsyncLocal<CultureInfo?> _asyncLocalCulture = new();
    private static volatile CultureInfo _globalCulture = CultureInfo.CurrentCulture;

    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _messages = new(
        new Dictionary<string, ConcurrentDictionary<string, string>>
        {
            ["en"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} cannot be null.",
                ["NotEmpty"] = "{0} cannot be empty.",
                ["NotDefault"] = "{0} cannot be default value.",
                ["Length"] = "{0} must be between {1} and {2} characters.",
                ["MinLength"] = "{0} must be at least {1} characters.",
                ["MaxLength"] = "{0} must be at most {1} characters.",
                ["Email"] = "{0} must be a valid email address.",
                ["Url"] = "{0} must be a valid URL.",
                ["Pattern"] = "{0} does not match the required pattern.",
                ["GreaterThan"] = "{0} must be greater than {1}.",
                ["LessThan"] = "{0} must be less than {1}.",
                ["InRange"] = "{0} must be between {1} and {2}.",
                ["Positive"] = "{0} must be positive.",
                ["NotNegative"] = "{0} cannot be negative.",
                ["NotZero"] = "{0} cannot be zero.",
                ["InPast"] = "{0} must be in the past.",
                ["InFuture"] = "{0} must be in the future.",
                ["CreditCard"] = "{0} is not a valid credit card number.",
                ["Iban"] = "{0} is not a valid IBAN.",
                ["PhoneNumber"] = "{0} is not a valid phone number.",
                ["Required"] = "{0} is required.",
                ["Unique"] = "{0} must be unique.",
                ["Exists"] = "{0} does not exist.",
                ["SqlInjection"] = "{0} contains potentially dangerous SQL content.",
                ["Xss"] = "{0} contains potentially dangerous script content.",
                ["PathTraversal"] = "{0} contains a path traversal sequence.",
                ["WeakPassword"] = "{0} does not meet password strength requirements.",
                ["TurkishId"] = "{0} is not a valid Turkish ID number.",
                ["TaxNumber"] = "{0} is not a valid tax number.",
                ["LicensePlate"] = "{0} is not a valid license plate.",
                ["DefaultStronglyTypedId"] = "{0} must not be the default value.",
                ["BusinessRuleBroken"] = "Business rule broken: {0}.",
                ["DomainInvariantViolated"] = "Domain invariant violated: {0}."
            }),
            ["tr"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} boş olamaz.",
                ["NotEmpty"] = "{0} boş veya sadece boşluk olamaz.",
                ["NotDefault"] = "{0} varsayılan değer olamaz.",
                ["Length"] = "{0} {1} ile {2} karakter arasında olmalıdır.",
                ["MinLength"] = "{0} en az {1} karakter olmalıdır.",
                ["MaxLength"] = "{0} en fazla {1} karakter olabilir.",
                ["Email"] = "{0} geçerli bir e-posta adresi olmalıdır.",
                ["Url"] = "{0} geçerli bir URL olmalıdır.",
                ["Pattern"] = "{0} gerekli desene uymuyor.",
                ["GreaterThan"] = "{0} {1} değerinden büyük olmalıdır.",
                ["LessThan"] = "{0} {1} değerinden küçük olmalıdır.",
                ["InRange"] = "{0} {1} ile {2} arasında olmalıdır.",
                ["Positive"] = "{0} pozitif olmalıdır.",
                ["NotNegative"] = "{0} negatif olamaz.",
                ["NotZero"] = "{0} sıfır olamaz.",
                ["InPast"] = "{0} geçmiş bir tarih olmalıdır.",
                ["InFuture"] = "{0} gelecek bir tarih olmalıdır.",
                ["CreditCard"] = "{0} geçerli bir kredi kartı numarası değil.",
                ["Iban"] = "{0} geçerli bir IBAN değil.",
                ["PhoneNumber"] = "{0} geçerli bir telefon numarası değil.",
                ["Required"] = "{0} gereklidir.",
                ["Unique"] = "{0} benzersiz olmalıdır.",
                ["Exists"] = "{0} mevcut değil.",
                ["SqlInjection"] = "{0} tehlikeli SQL içeriği barındırıyor.",
                ["Xss"] = "{0} tehlikeli script içeriği barındırıyor.",
                ["PathTraversal"] = "{0} dizin geçiş dizisi içeriyor.",
                ["WeakPassword"] = "{0} şifre güvenlik gereksinimlerini karşılamıyor.",
                ["TurkishId"] = "{0} geçerli bir TC Kimlik Numarası değil.",
                ["TaxNumber"] = "{0} geçerli bir vergi numarası değil.",
                ["LicensePlate"] = "{0} geçerli bir plaka değil.",
                ["DefaultStronglyTypedId"] = "{0} varsayılan değer olmamalıdır.",
                ["BusinessRuleBroken"] = "İş kuralı ihlal edildi: {0}.",
                ["DomainInvariantViolated"] = "Alan değişmezi ihlal edildi: {0}."
            }),
            ["de"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} darf nicht null sein.",
                ["NotEmpty"] = "{0} darf nicht leer sein.",
                ["NotDefault"] = "{0} darf nicht der Standardwert sein.",
                ["Length"] = "{0} muss zwischen {1} und {2} Zeichen lang sein.",
                ["MinLength"] = "{0} muss mindestens {1} Zeichen lang sein.",
                ["MaxLength"] = "{0} darf höchstens {1} Zeichen lang sein.",
                ["Email"] = "{0} muss eine gültige E-Mail-Adresse sein.",
                ["Url"] = "{0} muss eine gültige URL sein.",
                ["Pattern"] = "{0} entspricht nicht dem erforderlichen Muster.",
                ["GreaterThan"] = "{0} muss größer als {1} sein.",
                ["LessThan"] = "{0} muss kleiner als {1} sein.",
                ["InRange"] = "{0} muss zwischen {1} und {2} liegen.",
                ["Positive"] = "{0} muss positiv sein.",
                ["NotNegative"] = "{0} darf nicht negativ sein.",
                ["NotZero"] = "{0} darf nicht null sein.",
                ["InPast"] = "{0} muss in der Vergangenheit liegen.",
                ["InFuture"] = "{0} muss in der Zukunft liegen.",
                ["CreditCard"] = "{0} ist keine gültige Kreditkartennummer.",
                ["Iban"] = "{0} ist keine gültige IBAN.",
                ["PhoneNumber"] = "{0} ist keine gültige Telefonnummer.",
                ["Required"] = "{0} ist erforderlich.",
                ["Unique"] = "{0} muss eindeutig sein.",
                ["Exists"] = "{0} existiert nicht.",
                ["SqlInjection"] = "{0} enthält potenziell gefährlichen SQL-Inhalt.",
                ["Xss"] = "{0} enthält potenziell gefährlichen Skript-Inhalt.",
                ["PathTraversal"] = "{0} enthält eine Pfad-Traversierungssequenz.",
                ["WeakPassword"] = "{0} erfüllt nicht die Anforderungen an die Passwortstärke.",
                ["TurkishId"] = "{0} ist keine gültige türkische ID-Nummer.",
                ["TaxNumber"] = "{0} ist keine gültige Steuernummer.",
                ["LicensePlate"] = "{0} ist kein gültiges Kennzeichen.",
                ["DefaultStronglyTypedId"] = "{0} darf nicht der Standardwert sein.",
                ["BusinessRuleBroken"] = "Geschäftsregel verletzt: {0}.",
                ["DomainInvariantViolated"] = "Domäneninvariante verletzt: {0}."
            }),
            ["fr"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} ne peut pas être null.",
                ["NotEmpty"] = "{0} ne peut pas être vide.",
                ["NotDefault"] = "{0} ne peut pas être la valeur par défaut.",
                ["Length"] = "{0} doit contenir entre {1} et {2} caractères.",
                ["MinLength"] = "{0} doit contenir au moins {1} caractères.",
                ["MaxLength"] = "{0} doit contenir au plus {1} caractères.",
                ["Email"] = "{0} doit être une adresse e-mail valide.",
                ["Url"] = "{0} doit être une URL valide.",
                ["Pattern"] = "{0} ne correspond pas au modèle requis.",
                ["GreaterThan"] = "{0} doit être supérieur à {1}.",
                ["LessThan"] = "{0} doit être inférieur à {1}.",
                ["InRange"] = "{0} doit être compris entre {1} et {2}.",
                ["Positive"] = "{0} doit être positif.",
                ["NotNegative"] = "{0} ne peut pas être négatif.",
                ["NotZero"] = "{0} ne peut pas être zéro.",
                ["InPast"] = "{0} doit être dans le passé.",
                ["InFuture"] = "{0} doit être dans le futur.",
                ["CreditCard"] = "{0} n'est pas un numéro de carte de crédit valide.",
                ["Iban"] = "{0} n'est pas un IBAN valide.",
                ["PhoneNumber"] = "{0} n'est pas un numéro de téléphone valide.",
                ["Required"] = "{0} est requis.",
                ["Unique"] = "{0} doit être unique.",
                ["Exists"] = "{0} n'existe pas.",
                ["SqlInjection"] = "{0} contient du contenu SQL potentiellement dangereux.",
                ["Xss"] = "{0} contient du contenu script potentiellement dangereux.",
                ["PathTraversal"] = "{0} contient une séquence de traversée de chemin.",
                ["WeakPassword"] = "{0} ne répond pas aux exigences de robustesse du mot de passe.",
                ["TurkishId"] = "{0} n'est pas un numéro d'identité turc valide.",
                ["TaxNumber"] = "{0} n'est pas un numéro fiscal valide.",
                ["LicensePlate"] = "{0} n'est pas une plaque d'immatriculation valide.",
                ["DefaultStronglyTypedId"] = "{0} ne doit pas être la valeur par défaut.",
                ["BusinessRuleBroken"] = "Règle métier violée : {0}.",
                ["DomainInvariantViolated"] = "Invariant de domaine violé : {0}."
            }),
            ["es"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} no puede ser nulo.",
                ["NotEmpty"] = "{0} no puede estar vacío.",
                ["NotDefault"] = "{0} no puede ser el valor predeterminado.",
                ["Length"] = "{0} debe tener entre {1} y {2} caracteres.",
                ["MinLength"] = "{0} debe tener al menos {1} caracteres.",
                ["MaxLength"] = "{0} debe tener como máximo {1} caracteres.",
                ["Email"] = "{0} debe ser una dirección de correo válida.",
                ["Url"] = "{0} debe ser una URL válida.",
                ["Pattern"] = "{0} no coincide con el patrón requerido.",
                ["GreaterThan"] = "{0} debe ser mayor que {1}.",
                ["LessThan"] = "{0} debe ser menor que {1}.",
                ["InRange"] = "{0} debe estar entre {1} y {2}.",
                ["Positive"] = "{0} debe ser positivo.",
                ["NotNegative"] = "{0} no puede ser negativo.",
                ["NotZero"] = "{0} no puede ser cero.",
                ["InPast"] = "{0} debe ser una fecha pasada.",
                ["InFuture"] = "{0} debe ser una fecha futura.",
                ["CreditCard"] = "{0} no es un número de tarjeta de crédito válido.",
                ["Iban"] = "{0} no es un IBAN válido.",
                ["PhoneNumber"] = "{0} no es un número de teléfono válido.",
                ["Required"] = "{0} es obligatorio.",
                ["Unique"] = "{0} debe ser único.",
                ["Exists"] = "{0} no existe.",
                ["SqlInjection"] = "{0} contiene contenido SQL potencialmente peligroso.",
                ["Xss"] = "{0} contiene contenido de script potencialmente peligroso.",
                ["PathTraversal"] = "{0} contiene una secuencia de recorrido de ruta.",
                ["WeakPassword"] = "{0} no cumple los requisitos de seguridad de la contraseña.",
                ["TurkishId"] = "{0} no es un número de identidad turco válido.",
                ["TaxNumber"] = "{0} no es un número fiscal válido.",
                ["LicensePlate"] = "{0} no es una matrícula válida.",
                ["DefaultStronglyTypedId"] = "{0} no debe ser el valor predeterminado.",
                ["BusinessRuleBroken"] = "Regla de negocio infringida: {0}.",
                ["DomainInvariantViolated"] = "Invariante de dominio infringida: {0}."
            }),
            ["pt"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} não pode ser nulo.",
                ["NotEmpty"] = "{0} não pode estar vazio.",
                ["NotDefault"] = "{0} não pode ser o valor padrão.",
                ["Length"] = "{0} deve ter entre {1} e {2} caracteres.",
                ["MinLength"] = "{0} deve ter pelo menos {1} caracteres.",
                ["MaxLength"] = "{0} deve ter no máximo {1} caracteres.",
                ["Email"] = "{0} deve ser um endereço de e-mail válido.",
                ["Url"] = "{0} deve ser uma URL válida.",
                ["Pattern"] = "{0} não corresponde ao padrão exigido.",
                ["GreaterThan"] = "{0} deve ser maior que {1}.",
                ["LessThan"] = "{0} deve ser menor que {1}.",
                ["InRange"] = "{0} deve estar entre {1} e {2}.",
                ["Positive"] = "{0} deve ser positivo.",
                ["NotNegative"] = "{0} não pode ser negativo.",
                ["NotZero"] = "{0} não pode ser zero.",
                ["InPast"] = "{0} deve ser uma data no passado.",
                ["InFuture"] = "{0} deve ser uma data no futuro.",
                ["CreditCard"] = "{0} não é um número de cartão de crédito válido.",
                ["Iban"] = "{0} não é um IBAN válido.",
                ["PhoneNumber"] = "{0} não é um número de telefone válido.",
                ["Required"] = "{0} é obrigatório.",
                ["Unique"] = "{0} deve ser único.",
                ["Exists"] = "{0} não existe.",
                ["SqlInjection"] = "{0} contém conteúdo SQL potencialmente perigoso.",
                ["Xss"] = "{0} contém conteúdo de script potencialmente perigoso.",
                ["PathTraversal"] = "{0} contém uma sequência de travessia de caminho.",
                ["WeakPassword"] = "{0} não atende aos requisitos de força da senha.",
                ["TurkishId"] = "{0} não é um número de identidade turco válido.",
                ["TaxNumber"] = "{0} não é um número fiscal válido.",
                ["LicensePlate"] = "{0} não é uma placa válida.",
                ["DefaultStronglyTypedId"] = "{0} não pode ser o valor padrão.",
                ["BusinessRuleBroken"] = "Regra de negócio violada: {0}.",
                ["DomainInvariantViolated"] = "Invariante de domínio violada: {0}."
            }),
            ["ar"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} لا يمكن أن يكون فارغاً.",
                ["NotEmpty"] = "{0} لا يمكن أن يكون خالياً.",
                ["NotDefault"] = "{0} لا يمكن أن يكون القيمة الافتراضية.",
                ["Length"] = "{0} يجب أن يكون بين {1} و {2} حرفاً.",
                ["MinLength"] = "{0} يجب أن يكون على الأقل {1} حرفاً.",
                ["MaxLength"] = "{0} يجب ألا يتجاوز {1} حرفاً.",
                ["Email"] = "{0} يجب أن يكون عنوان بريد إلكتروني صالح.",
                ["Url"] = "{0} يجب أن يكون عنوان URL صالح.",
                ["Pattern"] = "{0} لا يتطابق مع النمط المطلوب.",
                ["GreaterThan"] = "{0} يجب أن يكون أكبر من {1}.",
                ["LessThan"] = "{0} يجب أن يكون أقل من {1}.",
                ["InRange"] = "{0} يجب أن يكون بين {1} و {2}.",
                ["Positive"] = "{0} يجب أن يكون إيجابياً.",
                ["NotNegative"] = "{0} لا يمكن أن يكون سالباً.",
                ["NotZero"] = "{0} لا يمكن أن يكون صفراً.",
                ["InPast"] = "{0} يجب أن يكون تاريخاً في الماضي.",
                ["InFuture"] = "{0} يجب أن يكون تاريخاً في المستقبل.",
                ["CreditCard"] = "{0} ليس رقم بطاقة ائتمان صالح.",
                ["Iban"] = "{0} ليس رقم IBAN صالح.",
                ["PhoneNumber"] = "{0} ليس رقم هاتف صالح.",
                ["Required"] = "{0} مطلوب.",
                ["Unique"] = "{0} يجب أن يكون فريداً.",
                ["Exists"] = "{0} غير موجود.",
                ["SqlInjection"] = "{0} يحتوي على محتوى SQL قد يكون خطيراً.",
                ["Xss"] = "{0} يحتوي على محتوى برمجي قد يكون خطيراً.",
                ["PathTraversal"] = "{0} يحتوي على تسلسل اجتياز المسار.",
                ["WeakPassword"] = "{0} لا يستوفي متطلبات قوة كلمة المرور.",
                ["TurkishId"] = "{0} ليس رقم هوية تركي صالح.",
                ["TaxNumber"] = "{0} ليس رقم ضريبي صالح.",
                ["LicensePlate"] = "{0} ليست لوحة ترخيص صالحة.",
                ["DefaultStronglyTypedId"] = "{0} يجب ألا يكون القيمة الافتراضية.",
                ["BusinessRuleBroken"] = "تم انتهاك قاعدة عمل: {0}.",
                ["DomainInvariantViolated"] = "تم انتهاك ثابت النطاق: {0}."
            }),
            ["ja"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} はnullにできません。",
                ["NotEmpty"] = "{0} は空にできません。",
                ["NotDefault"] = "{0} はデフォルト値にできません。",
                ["Length"] = "{0} は{1}文字から{2}文字の間である必要があります。",
                ["MinLength"] = "{0} は少なくとも{1}文字である必要があります。",
                ["MaxLength"] = "{0} は最大{1}文字までです。",
                ["Email"] = "{0} は有効なメールアドレスである必要があります。",
                ["Url"] = "{0} は有効なURLである必要があります。",
                ["Pattern"] = "{0} は必要なパターンに一致しません。",
                ["GreaterThan"] = "{0} は{1}より大きい必要があります。",
                ["LessThan"] = "{0} は{1}より小さい必要があります。",
                ["InRange"] = "{0} は{1}から{2}の間である必要があります。",
                ["Positive"] = "{0} は正の数である必要があります。",
                ["NotNegative"] = "{0} は負の数にできません。",
                ["NotZero"] = "{0} はゼロにできません。",
                ["InPast"] = "{0} は過去の日付である必要があります。",
                ["InFuture"] = "{0} は未来の日付である必要があります。",
                ["CreditCard"] = "{0} は有効なクレジットカード番号ではありません。",
                ["Iban"] = "{0} は有効なIBANではありません。",
                ["PhoneNumber"] = "{0} は有効な電話番号ではありません。",
                ["Required"] = "{0} は必須です。",
                ["Unique"] = "{0} は一意である必要があります。",
                ["Exists"] = "{0} は存在しません。",
                ["SqlInjection"] = "{0} に危険なSQLコンテンツが含まれている可能性があります。",
                ["Xss"] = "{0} に危険なスクリプトコンテンツが含まれている可能性があります。",
                ["PathTraversal"] = "{0} にパストラバーサルシーケンスが含まれています。",
                ["WeakPassword"] = "{0} はパスワード強度の要件を満たしていません。",
                ["TurkishId"] = "{0} は有効なトルコIDナンバーではありません。",
                ["TaxNumber"] = "{0} は有効な税番号ではありません。",
                ["LicensePlate"] = "{0} は有効なナンバープレートではありません。",
                ["DefaultStronglyTypedId"] = "{0} は既定値であってはなりません。",
                ["BusinessRuleBroken"] = "ビジネスルール違反: {0}。",
                ["DomainInvariantViolated"] = "ドメイン不変条件違反: {0}。"
            }),
            ["zh"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} 不能为空。",
                ["NotEmpty"] = "{0} 不能为空白。",
                ["NotDefault"] = "{0} 不能是默认值。",
                ["Length"] = "{0} 必须在 {1} 到 {2} 个字符之间。",
                ["MinLength"] = "{0} 必须至少 {1} 个字符。",
                ["MaxLength"] = "{0} 不能超过 {1} 个字符。",
                ["Email"] = "{0} 必须是有效的电子邮件地址。",
                ["Url"] = "{0} 必须是有效的 URL。",
                ["Pattern"] = "{0} 不匹配所需的模式。",
                ["GreaterThan"] = "{0} 必须大于 {1}。",
                ["LessThan"] = "{0} 必须小于 {1}。",
                ["InRange"] = "{0} 必须在 {1} 和 {2} 之间。",
                ["Positive"] = "{0} 必须是正数。",
                ["NotNegative"] = "{0} 不能是负数。",
                ["NotZero"] = "{0} 不能为零。",
                ["InPast"] = "{0} 必须是过去的日期。",
                ["InFuture"] = "{0} 必须是将来的日期。",
                ["CreditCard"] = "{0} 不是有效的信用卡号。",
                ["Iban"] = "{0} 不是有效的 IBAN。",
                ["PhoneNumber"] = "{0} 不是有效的电话号码。",
                ["Required"] = "{0} 是必填项。",
                ["Unique"] = "{0} 必须是唯一的。",
                ["Exists"] = "{0} 不存在。",
                ["SqlInjection"] = "{0} 包含潜在危险的 SQL 内容。",
                ["Xss"] = "{0} 包含潜在危险的脚本内容。",
                ["PathTraversal"] = "{0} 包含路径遍历序列。",
                ["WeakPassword"] = "{0} 不符合密码强度要求。",
                ["TurkishId"] = "{0} 不是有效的土耳其身份证号。",
                ["TaxNumber"] = "{0} 不是有效的税号。",
                ["LicensePlate"] = "{0} 不是有效的车牌号。",
                ["DefaultStronglyTypedId"] = "{0} 不能为默认值。",
                ["BusinessRuleBroken"] = "业务规则被破坏：{0}。",
                ["DomainInvariantViolated"] = "领域不变式被破坏：{0}。"
            }),
            ["ko"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0}은(는) null일 수 없습니다.",
                ["NotEmpty"] = "{0}은(는) 비어 있을 수 없습니다.",
                ["NotDefault"] = "{0}은(는) 기본값일 수 없습니다.",
                ["Length"] = "{0}은(는) {1}자에서 {2}자 사이여야 합니다.",
                ["MinLength"] = "{0}은(는) 최소 {1}자 이상이어야 합니다.",
                ["MaxLength"] = "{0}은(는) 최대 {1}자까지 가능합니다.",
                ["Email"] = "{0}은(는) 유효한 이메일 주소여야 합니다.",
                ["Url"] = "{0}은(는) 유효한 URL이어야 합니다.",
                ["Pattern"] = "{0}은(는) 필수 패턴과 일치하지 않습니다.",
                ["GreaterThan"] = "{0}은(는) {1}보다 커야 합니다.",
                ["LessThan"] = "{0}은(는) {1}보다 작아야 합니다.",
                ["InRange"] = "{0}은(는) {1}에서 {2} 사이여야 합니다.",
                ["Positive"] = "{0}은(는) 양수여야 합니다.",
                ["NotNegative"] = "{0}은(는) 음수일 수 없습니다.",
                ["NotZero"] = "{0}은(는) 0일 수 없습니다.",
                ["InPast"] = "{0}은(는) 과거 날짜여야 합니다.",
                ["InFuture"] = "{0}은(는) 미래 날짜여야 합니다.",
                ["CreditCard"] = "{0}은(는) 유효한 신용카드 번호가 아닙니다.",
                ["Iban"] = "{0}은(는) 유효한 IBAN이 아닙니다.",
                ["PhoneNumber"] = "{0}은(는) 유효한 전화번호가 아닙니다.",
                ["Required"] = "{0}은(는) 필수입니다.",
                ["Unique"] = "{0}은(는) 고유해야 합니다.",
                ["Exists"] = "{0}은(는) 존재하지 않습니다.",
                ["SqlInjection"] = "{0}에 잠재적으로 위험한 SQL 내용이 포함되어 있습니다.",
                ["Xss"] = "{0}에 잠재적으로 위험한 스크립트 내용이 포함되어 있습니다.",
                ["PathTraversal"] = "{0}에 경로 탐색 시퀀스가 포함되어 있습니다.",
                ["WeakPassword"] = "{0}이(가) 비밀번호 강도 요구 사항을 충족하지 않습니다.",
                ["TurkishId"] = "{0}은(는) 유효한 터키 신분증 번호가 아닙니다.",
                ["TaxNumber"] = "{0}은(는) 유효한 세금 번호가 아닙니다.",
                ["LicensePlate"] = "{0}은(는) 유효한 번호판이 아닙니다.",
                ["DefaultStronglyTypedId"] = "{0}은(는) 기본값이 아니어야 합니다.",
                ["BusinessRuleBroken"] = "비즈니스 규칙 위반: {0}.",
                ["DomainInvariantViolated"] = "도메인 불변식 위반: {0}."
            }),
            ["ru"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} не может быть null.",
                ["NotEmpty"] = "{0} не может быть пустым.",
                ["NotDefault"] = "{0} не может быть значением по умолчанию.",
                ["Length"] = "{0} должен быть от {1} до {2} символов.",
                ["MinLength"] = "{0} должен содержать не менее {1} символов.",
                ["MaxLength"] = "{0} должен содержать не более {1} символов.",
                ["Email"] = "{0} должен быть действительным адресом электронной почты.",
                ["Url"] = "{0} должен быть действительным URL.",
                ["Pattern"] = "{0} не соответствует требуемому шаблону.",
                ["GreaterThan"] = "{0} должен быть больше {1}.",
                ["LessThan"] = "{0} должен быть меньше {1}.",
                ["InRange"] = "{0} должен быть между {1} и {2}.",
                ["Positive"] = "{0} должен быть положительным.",
                ["NotNegative"] = "{0} не может быть отрицательным.",
                ["NotZero"] = "{0} не может быть нулём.",
                ["InPast"] = "{0} должен быть датой в прошлом.",
                ["InFuture"] = "{0} должен быть датой в будущем.",
                ["CreditCard"] = "{0} не является действительным номером кредитной карты.",
                ["Iban"] = "{0} не является действительным IBAN.",
                ["PhoneNumber"] = "{0} не является действительным номером телефона.",
                ["Required"] = "{0} обязательно для заполнения.",
                ["Unique"] = "{0} должен быть уникальным.",
                ["Exists"] = "{0} не существует.",
                ["SqlInjection"] = "{0} содержит потенциально опасное SQL-содержимое.",
                ["Xss"] = "{0} содержит потенциально опасное скриптовое содержимое.",
                ["PathTraversal"] = "{0} содержит последовательность обхода пути.",
                ["WeakPassword"] = "{0} не соответствует требованиям к надёжности пароля.",
                ["TurkishId"] = "{0} не является действительным турецким идентификационным номером.",
                ["TaxNumber"] = "{0} не является действительным налоговым номером.",
                ["LicensePlate"] = "{0} не является действительным номерным знаком.",
                ["DefaultStronglyTypedId"] = "{0} не должен быть значением по умолчанию.",
                ["BusinessRuleBroken"] = "Нарушено бизнес-правило: {0}.",
                ["DomainInvariantViolated"] = "Нарушен инвариант домена: {0}."
            }),
            ["nl"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} mag niet null zijn.",
                ["NotEmpty"] = "{0} mag niet leeg zijn.",
                ["NotDefault"] = "{0} mag niet de standaardwaarde zijn.",
                ["Length"] = "{0} moet tussen {1} en {2} tekens lang zijn.",
                ["MinLength"] = "{0} moet minstens {1} tekens lang zijn.",
                ["MaxLength"] = "{0} mag maximaal {1} tekens lang zijn.",
                ["Email"] = "{0} moet een geldig e-mailadres zijn.",
                ["Url"] = "{0} moet een geldige URL zijn.",
                ["Pattern"] = "{0} komt niet overeen met het vereiste patroon.",
                ["GreaterThan"] = "{0} moet groter zijn dan {1}.",
                ["LessThan"] = "{0} moet kleiner zijn dan {1}.",
                ["InRange"] = "{0} moet tussen {1} en {2} liggen.",
                ["Positive"] = "{0} moet positief zijn.",
                ["NotNegative"] = "{0} mag niet negatief zijn.",
                ["NotZero"] = "{0} mag niet nul zijn.",
                ["InPast"] = "{0} moet een datum in het verleden zijn.",
                ["InFuture"] = "{0} moet een datum in de toekomst zijn.",
                ["CreditCard"] = "{0} is geen geldig creditcardnummer.",
                ["Iban"] = "{0} is geen geldig IBAN.",
                ["PhoneNumber"] = "{0} is geen geldig telefoonnummer.",
                ["Required"] = "{0} is verplicht.",
                ["Unique"] = "{0} moet uniek zijn.",
                ["Exists"] = "{0} bestaat niet.",
                ["SqlInjection"] = "{0} bevat mogelijk gevaarlijke SQL-inhoud.",
                ["Xss"] = "{0} bevat mogelijk gevaarlijke scriptinhoud.",
                ["PathTraversal"] = "{0} bevat een pad-traversalreeks.",
                ["WeakPassword"] = "{0} voldoet niet aan de wachtwoordsterkte-eisen.",
                ["TurkishId"] = "{0} is geen geldig Turks identiteitsnummer.",
                ["TaxNumber"] = "{0} is geen geldig belastingnummer.",
                ["LicensePlate"] = "{0} is geen geldig kenteken.",
                ["DefaultStronglyTypedId"] = "{0} mag niet de standaardwaarde zijn.",
                ["BusinessRuleBroken"] = "Bedrijfsregel geschonden: {0}.",
                ["DomainInvariantViolated"] = "Domein-invariant geschonden: {0}."
            }),
            ["pl"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} nie może być null.",
                ["NotEmpty"] = "{0} nie może być puste.",
                ["NotDefault"] = "{0} nie może być wartością domyślną.",
                ["Length"] = "{0} musi mieć od {1} do {2} znaków.",
                ["MinLength"] = "{0} musi mieć co najmniej {1} znaków.",
                ["MaxLength"] = "{0} może mieć maksymalnie {1} znaków.",
                ["Email"] = "{0} musi być prawidłowym adresem e-mail.",
                ["Url"] = "{0} musi być prawidłowym adresem URL.",
                ["Pattern"] = "{0} nie pasuje do wymaganego wzorca.",
                ["GreaterThan"] = "{0} musi być większe niż {1}.",
                ["LessThan"] = "{0} musi być mniejsze niż {1}.",
                ["InRange"] = "{0} musi być między {1} a {2}.",
                ["Positive"] = "{0} musi być liczbą dodatnią.",
                ["NotNegative"] = "{0} nie może być liczbą ujemną.",
                ["NotZero"] = "{0} nie może być zerem.",
                ["InPast"] = "{0} musi być datą z przeszłości.",
                ["InFuture"] = "{0} musi być datą z przyszłości.",
                ["CreditCard"] = "{0} nie jest prawidłowym numerem karty kredytowej.",
                ["Iban"] = "{0} nie jest prawidłowym numerem IBAN.",
                ["PhoneNumber"] = "{0} nie jest prawidłowym numerem telefonu.",
                ["Required"] = "{0} jest wymagane.",
                ["Unique"] = "{0} musi być unikalne.",
                ["Exists"] = "{0} nie istnieje.",
                ["SqlInjection"] = "{0} zawiera potencjalnie niebezpieczną treść SQL.",
                ["Xss"] = "{0} zawiera potencjalnie niebezpieczną treść skryptową.",
                ["PathTraversal"] = "{0} zawiera sekwencję przejścia ścieżki.",
                ["WeakPassword"] = "{0} nie spełnia wymagań dotyczących siły hasła.",
                ["TurkishId"] = "{0} nie jest prawidłowym tureckim numerem identyfikacyjnym.",
                ["TaxNumber"] = "{0} nie jest prawidłowym numerem podatkowym.",
                ["LicensePlate"] = "{0} nie jest prawidłową tablicą rejestracyjną.",
                ["DefaultStronglyTypedId"] = "{0} nie może być wartością domyślną.",
                ["BusinessRuleBroken"] = "Naruszono regułę biznesową: {0}.",
                ["DomainInvariantViolated"] = "Naruszono niezmiennik domeny: {0}."
            }),
            ["it"] = new(new Dictionary<string, string>
            {
                ["NotNull"] = "{0} non può essere nullo.",
                ["NotEmpty"] = "{0} non può essere vuoto.",
                ["NotDefault"] = "{0} non può essere il valore predefinito.",
                ["Length"] = "{0} deve essere compreso tra {1} e {2} caratteri.",
                ["MinLength"] = "{0} deve contenere almeno {1} caratteri.",
                ["MaxLength"] = "{0} deve contenere al massimo {1} caratteri.",
                ["Email"] = "{0} deve essere un indirizzo email valido.",
                ["Url"] = "{0} deve essere un URL valido.",
                ["Pattern"] = "{0} non corrisponde al formato richiesto.",
                ["GreaterThan"] = "{0} deve essere maggiore di {1}.",
                ["LessThan"] = "{0} deve essere minore di {1}.",
                ["InRange"] = "{0} deve essere compreso tra {1} e {2}.",
                ["Positive"] = "{0} deve essere positivo.",
                ["NotNegative"] = "{0} non può essere negativo.",
                ["NotZero"] = "{0} non può essere zero.",
                ["InPast"] = "{0} deve essere una data passata.",
                ["InFuture"] = "{0} deve essere una data futura.",
                ["CreditCard"] = "{0} non è un numero di carta di credito valido.",
                ["Iban"] = "{0} non è un IBAN valido.",
                ["PhoneNumber"] = "{0} non è un numero di telefono valido.",
                ["Required"] = "{0} è obbligatorio.",
                ["Unique"] = "{0} deve essere univoco.",
                ["Exists"] = "{0} non esiste.",
                ["SqlInjection"] = "{0} contiene contenuto SQL potenzialmente pericoloso.",
                ["Xss"] = "{0} contiene codice script potenzialmente pericoloso.",
                ["PathTraversal"] = "{0} contiene una sequenza di path traversal.",
                ["WeakPassword"] = "{0} non soddisfa i requisiti di complessità della password.",
                ["TurkishId"] = "{0} non è un numero di documento turco valido.",
                ["TaxNumber"] = "{0} non è un numero di identificazione fiscale valido.",
                ["LicensePlate"] = "{0} non è una targa valida.",
                ["DefaultStronglyTypedId"] = "{0} non deve essere il valore predefinito.",
                ["BusinessRuleBroken"] = "Regola di business violata: {0}.",
                ["DomainInvariantViolated"] = "Invariante di dominio violata: {0}."
            })
        });

    /// <summary>
    /// Gets the current effective culture (async-local takes priority over global).
    /// </summary>
    public static CultureInfo CurrentCulture => _asyncLocalCulture.Value ?? _globalCulture;

    /// <summary>
    /// Sets the culture globally for validation messages.
    /// </summary>
    public static void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _globalCulture = culture;
    }

    /// <summary>
    /// Sets the culture globally using culture name.
    /// </summary>
    public static void SetCulture(string cultureName)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        _globalCulture = new CultureInfo(cultureName);
    }

    /// <summary>
    /// Sets the culture for the current async context (thread-safe, does not affect other threads).
    /// </summary>
    public static void SetCultureForCurrentScope(CultureInfo culture)
    {
        _asyncLocalCulture.Value = culture;
    }

    /// <summary>
    /// Sets a custom message resolver function.
    /// </summary>
    public static void SetMessageResolver(Func<string, string?, string> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _messageResolver = resolver;
    }

    /// <summary>
    /// Adds or updates messages for a specific culture (thread-safe).
    /// </summary>
    public static void AddMessages(string cultureName, Dictionary<string, string> messages)
    {
        var cultureMessages = _messages.GetOrAdd(cultureName, _ => new ConcurrentDictionary<string, string>());
        foreach (var kvp in messages)
        {
            cultureMessages[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Gets a localized message using the current culture.
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        var template = _messageResolver(key, CurrentCulture.TwoLetterISOLanguageName);
        return string.Format(CurrentCulture, template, args);
    }

    /// <summary>
    /// Gets a localized message for a specific culture.
    /// </summary>
    public static string Get(string key, CultureInfo culture, params object[] args)
    {
        var template = _messageResolver(key, culture.TwoLetterISOLanguageName);
        return string.Format(culture, template, args);
    }

    private static string DefaultMessageResolver(string key, string? cultureName)
    {
        cultureName ??= "en";

        if (_messages.TryGetValue(cultureName, out var cultureMessages) &&
            cultureMessages.TryGetValue(key, out var message))
        {
            return message;
        }

        // Fallback to English
        if (_messages.TryGetValue("en", out var englishMessages) &&
            englishMessages.TryGetValue(key, out var englishMessage))
        {
            return englishMessage;
        }

        return key;
    }
}

/// <summary>
/// Extension methods for using localized validation messages.
/// </summary>
public static class LocalizedGuardExtensions
{
    /// <summary>
    /// Gets a localized validation message.
    /// </summary>
    public static string Localized(this string key, params object[] args)
    {
        return ValidationMessages.Get(key, args);
    }
}
