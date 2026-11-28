using Meitou.Modding;

namespace Nue.SearchCompare;

internal static class NueText
{
    private static Func<string, string>? translate;

    internal static void Register(ModContext context)
    {
        var rows = Messages.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('|')).ToArray();
        string[] languages = ["de", "fr", "zh", "ru", "ja", "ko"];
        string[] locales = ["de_DE", "fr_FR", "zh_CN", "ru_RU", "ja_JP", "ko_KR"];
        for (int i = 0; i < languages.Length; i++)
        {
            int column = i + 1;
            var messages = rows.Select(row => new KeyValuePair<string, string>(row[0], row[column])).ToArray();
            context.AddTranslations(languages[i], messages, null);
            context.AddTranslations(locales[i], messages, null);
        }
        translate = context.Tr;
    }

    internal static string Get(string english) => translate?.Invoke(english) ?? english;

    private const string Messages = """
Search|Suchen|Rechercher|搜索|Поиск|検索|검색
SHOW MODIFIERS|MODIFIKATOREN ANZEIGEN|AFFICHER LES MODIFICATEURS|显示修正值|ПОКАЗАТЬ МОДИФИКАТОРЫ|補正値を表示|보정치 표시
HIDE MODIFIERS|MODIFIKATOREN AUSBLENDEN|MASQUER LES MODIFICATEURS|隐藏修正值|СКРЫТЬ МОДИФИКАТОРЫ|補正値を非表示|보정치 숨기기
Search items|Gegenstände suchen|Rechercher des objets|搜索物品|Поиск предметов|アイテムを検索|아이템 검색
Search all items|Alle Gegenstände suchen|Rechercher tous les objets|搜索所有物品|Поиск по всем предметам|すべてのアイテムを検索|모든 아이템 검색
Search all blueprints|Alle Baupläne suchen|Rechercher tous les plans|搜索所有蓝图|Поиск по всем чертежам|すべての設計図を検索|모든 설계도 검색
EQUIPPED|AUSGERÜSTET|ÉQUIPÉ|已装备|ЭКИПИРОВАНО|装備中|장착 중
INVENTORY|INVENTAR|INVENTAIRE|物品栏|ИНВЕНТАРЬ|インベントリ|인벤토리
BACKPACK|RUCKSACK|SAC À DOS|背包|РЮКЗАК|バックパック|배낭
ARRANGE|SORTIEREN|RANGER|整理|УПОРЯДОЧИТЬ|整理|정리
PRIMARY|HAUPTWAFFE|ARME PRINCIPALE|主武器|ОСНОВНОЕ|主武器|주 무기
SECONDARY|NEBENWAFFE|ARME SECONDAIRE|副武器|ДОПОЛНИТЕЛЬНОЕ|副武器|보조 무기
SHIRT|HEMD|CHEMISE|内衬|РУБАШКА|シャツ|상의
HELMET|HELM|CASQUE|头盔|ШЛЕМ|兜|투구
PANTS|HOSE|PANTALON|裤子|ШТАНЫ|ズボン|바지
BODY ARMOR|KÖRPERRÜSTUNG|ARMURE DE TORSE|躯干护甲|НАГРУДНИК|胴防具|몸통 방어구
BOOTS|STIEFEL|BOTTES|靴子|САПОГИ|ブーツ|장화
BELT|GÜRTEL|CEINTURE|腰带|ПОЯС|ベルト|허리띠
ARMS|ARME|BRAS|手臂|РУКИ|腕|팔
LEGS|BEINE|JAMBES|腿部|НОГИ|脚|다리
OPEN BAG|RUCKSACK ÖFFNEN|OUVRIR LE SAC|打开背包|ОТКРЫТЬ РЮКЗАК|バッグを開く|가방 열기
CHARACTER INSPECTOR|FIGURENINSPEKTION|INSPECTION DU PERSONNAGE|角色检视|ОСМОТР ПЕРСОНАЖА|キャラクター詳細|캐릭터 상세
Money|Geld|Argent|金钱|Деньги|所持金|소지금
STATS|WERTE|STATS|属性|ХАРАКТЕРИСТИКИ|能力値|능력치
HEALTH|GESUNDHEIT|SANTÉ|生命值|ЗДОРОВЬЕ|体力|체력
Click to toggle view between stats and health bars|Klicken, um zwischen Werten und Gesundheitsbalken zu wechseln|Cliquez pour basculer entre les statistiques et les barres de santé|点击切换属性和生命值条|Нажмите, чтобы переключиться между характеристиками и шкалами здоровья|クリックで能力値と体力バーの表示を切り替えます|클릭하면 능력치와 체력 막대 표시가 전환됩니다
Blood|Blut|Sang|血液|Кровь|血液|혈액
Oil|Öl|Huile|机油|Масло|オイル|오일
Hunger|Hunger|Faim|饥饿|Голод|空腹|허기
Idle|Leerlauf|Au repos|待机|Покой|待機|대기
Attack stance|Angriffshaltung|Posture d’attaque|攻击姿势|Атакующая стойка|攻撃の構え|공격 자세
Sitting on mat|Auf einer Matte sitzend|Assis sur une natte|席地而坐|Сидя на циновке|敷物に座る|깔개에 앉기
Sleeping / lying|Schlafen / Liegen|Dormir / s’allonger|睡眠 / 躺卧|Сон / лёжа|睡眠・横になる|자기 / 눕기
Desert|Wüste|Désert|沙漠|Пустыня|砂漠|사막
Swamp|Sumpf|Marais|沼泽|Болото|沼地|늪
Black|Schwarz|Noir|黑色|Чёрный|黒|검정
BODY FIGURE|KÖRPERANSICHT|VUE DU CORPS|身体示意图|СХЕМА ТЕЛА|身体図|신체 그림
ARMOUR CHART|RÜSTUNGSDIAGRAMM|GRAPHIQUE D’ARMURE|护甲雷达图|ДИАГРАММА БРОНИ|防具チャート|방어구 차트
EST. ARMOUR PROTECTION|GESCHÄTZTER RÜSTUNGSSCHUTZ|PROTECTION D’ARMURE ESTIMÉE|预计护甲防护|ОЦЕНКА ЗАЩИТЫ БРОНИ|防具の推定防御性能|방어구 보호 추정치
ARMOUR PROTECTION|RÜSTUNGSSCHUTZ|PROTECTION D’ARMURE|护甲防护|ЗАЩИТА БРОНИ|防具の防御性能|방어구 보호
No body-part data available.|Keine Körperteildaten verfügbar.|Aucune donnée sur les parties du corps.|没有可用的身体部位数据。|Нет данных о частях тела.|部位データがありません。|신체 부위 정보가 없습니다.
Armour chart unavailable.|Rüstungsdiagramm nicht verfügbar.|Graphique d’armure indisponible.|护甲雷达图不可用。|Диаграмма брони недоступна.|防具チャートを表示できません。|방어구 차트를 표시할 수 없습니다.
Coverage|Abdeckung|Couverture|覆盖率|Покрытие|カバー率|보호 범위
Armour coverage|Rüstungsabdeckung|Couverture d’armure|护甲覆盖率|Покрытие брони|防具カバー率|방어구 보호 범위
Cut resistance|Schnittschutz|Résistance aux coupures|切割抗性|Сопротивление порезам|斬撃耐性|절단 저항
Cut damage resistance|Schnittschadensschutz|Résistance aux dégâts tranchants|切割伤害抗性|Сопротивление режущему урону|斬撃ダメージ耐性|절단 피해 저항
Blunt resistance|Schlagschutz|Résistance aux chocs|钝击抗性|Сопротивление ударам|打撃耐性|타격 저항
Blunt damage resistance|Schlagschadensschutz|Résistance aux dégâts contondants|钝击伤害抗性|Сопротивление дробящему урону|打撃ダメージ耐性|타격 피해 저항
Harpoon resistance|Harpunenschutz|Résistance aux harpons|鱼叉抗性|Сопротивление гарпунам|銛への耐性|작살 저항
Cut|Schnitt|Tranchant|切割|Режущий|斬撃|절단
Blunt|Schlag|Contondant|钝击|Дробящий|打撃|타격
Harpoon|Harpune|Harpon|鱼叉|Гарпун|銛|작살
Click to change {0} colour|Klicken, um die Farbe zu ändern: {0}|Cliquez pour changer la couleur : {0}|点击更改颜色：{0}|Нажмите, чтобы изменить цвет: {0}|クリックで色を変更：{0}|클릭하여 색상 변경: {0}
BODY PARTS|KÖRPERTEILE|PARTIES DU CORPS|身体部位|ЧАСТИ ТЕЛА|身体部位|신체 부위
ARMOUR ON THIS PART|RÜSTUNG AN DIESEM KÖRPERTEIL|ARMURE SUR CETTE PARTIE|该部位的护甲|БРОНЯ НА ЭТОЙ ЧАСТИ ТЕЛА|この部位を覆う防具|이 부위를 보호하는 방어구
No armour covers this part.|Dieses Körperteil ist ungeschützt.|Aucune armure ne couvre cette partie.|没有护甲覆盖该部位。|Эта часть тела не прикрыта бронёй.|この部位を覆う防具はありません。|이 부위를 보호하는 방어구가 없습니다.
Head|Kopf|Tête|头部|Голова|頭|머리
Chest|Brust|Torse|胸部|Грудь|胸|가슴
Stomach|Bauch|Ventre|腹部|Живот|腹|복부
Left Arm|Linker Arm|Bras gauche|左臂|Левая рука|左腕|왼팔
Right Arm|Rechter Arm|Bras droit|右臂|Правая рука|右腕|오른팔
Left Leg|Linkes Bein|Jambe gauche|左腿|Левая нога|左脚|왼다리
Right Leg|Rechtes Bein|Jambe droite|右腿|Правая нога|右脚|오른다리
BACK|ZURÜCK|RETOUR|返回|НАЗАД|戻る|뒤로
PREV|ZURÜCK|PRÉC.|上一页|ПРЕД.|前へ|이전
NEXT|WEITER|SUIV.|下一页|СЛЕД.|次へ|다음
Core Features|Kernfunktionen|Fonctions principales|核心功能|Основные функции|基本機能|핵심 기능
Mod Compatibility|Mod-Kompatibilität|Compatibilité des mods|模组兼容性|Совместимость модов|MOD互換性|모드 호환성
Takes effect after restarting MeitouClient.|Wirksam nach einem Neustart von MeitouClient.|Prend effet après le redémarrage de MeitouClient.|重启 MeitouClient 后生效。|Вступает в силу после перезапуска MeitouClient.|MeitouClientの再起動後に反映されます。|MeitouClient를 다시 시작하면 적용됩니다.
Tooltips follow the cursor|Tooltips folgen dem Mauszeiger|Les infobulles suivent le curseur|提示框跟随光标|Подсказки следуют за курсором|ツールチップをカーソルに追従させる|툴팁이 커서를 따라다님
HUD compass|HUD-Kompass|Boussole du HUD|HUD 罗盘|Компас в интерфейсе|HUDコンパス|HUD 나침반
Health and Stats tabs and building panel|Gesundheits- und Werte-Tabs sowie Gebäudepanel|Onglets Santé et Stats et panneau des bâtiments|生命值、属性标签页及建筑面板|Вкладки здоровья и характеристик и панель зданий|体力・能力値タブと建物パネル|체력·능력치 탭 및 건물 패널
Hover stats|Werte beim Überfahren anzeigen|Stats au survol|悬停显示属性|Характеристики при наведении|カーソル下の能力値|마우스 오버 시 능력치 표시
↳ Show character hover outline|↳ Figurenumriss beim Überfahren|↳ Afficher le contour du personnage au survol|↳ 悬停显示角色轮廓|↳ Контур персонажа при наведении|↳ カーソル下のキャラクターを縁取り|↳ 마우스 오버 시 캐릭터 윤곽선 표시
Requires Hover stats and the game's hover outline.|Benötigt die Werteanzeige beim Überfahren und die Umrisseinstellung des Spiels.|Nécessite les stats au survol et le contour au survol du jeu.|需要启用悬停属性及游戏的悬停轮廓。|Требует включить характеристики и контур при наведении.|カーソル下の能力値表示とゲームの縁取り設定が必要です。|능력치 표시와 게임의 마우스 오버 윤곽선이 필요합니다.
Container highlights|Behälter hervorheben|Surbrillance des conteneurs|容器高亮|Подсветка контейнеров|容器の強調表示|보관함 강조 표시
↳ Additional container highlight key|↳ Zusatztaste für Behälterumrisse|↳ Touche supplémentaire de surbrillance|↳ 容器高亮附加按键|↳ Дополнительная клавиша подсветки|↳ 容器強調表示の追加キー|↳ 보관함 강조 표시 보조 키
Leave blank to use the game's highlight key (Alt by default). Enter a key or chord, such as H or Ctrl+H.|Leer lassen, um die Spieltaste zu verwenden (standardmäßig Alt). Beispiel: H oder Ctrl+H.|Laissez vide pour utiliser la touche du jeu (Alt par défaut). Exemple : H ou Ctrl+H.|留空则使用游戏的高亮键（默认 Alt）。可输入 H 或 Ctrl+H 等按键。|Оставьте пустым, чтобы использовать клавишу игры (по умолчанию Alt). Пример: H или Ctrl+H.|空欄ではゲームの強調表示キー（既定ではAlt）を使います。HやCtrl+Hなどを入力できます。|비워 두면 게임의 강조 표시 키(기본값 Alt)를 사용합니다. H 또는 Ctrl+H를 입력할 수 있습니다.
Examine|Untersuchen|Examiner|查看|Осмотреть|調べる|조사
Racial XP|Spezies-EP|XP raciale|种族经验倍率|Расовый опыт|種族別経験値|종족 경험치
Inventory inspection and avatar|Inventarinspektion und Avatar|Inspection de l’inventaire et avatar|物品栏检视及角色预览|Осмотр инвентаря и аватар|インベントリ詳細とアバター|인벤토리 상세 및 아바타
Inventory, crafting and research search|Inventar-, Herstellungs- und Forschungssuche|Recherche dans l’inventaire, la fabrication et la recherche|物品栏、制作及研究搜索|Поиск в инвентаре, ремесле и исследованиях|インベントリ・製作・研究の検索|인벤토리·제작·연구 검색
Gear comparison|Ausrüstungsvergleich|Comparaison d’équipement|装备对比|Сравнение снаряжения|装備比較|장비 비교
Character inspector|Figureninspektion|Inspection du personnage|角色检视|Осмотр персонажа|キャラクター詳細|캐릭터 상세
Skills window portrait|Porträt im Fertigkeitsfenster|Portrait dans la fenêtre des compétences|技能窗口角色头像|Портрет в окне навыков|スキル画面の肖像|기술 창 초상화
Item Markers compatibility|Kompatibilität mit Item Markers|Compatibilité avec Item Markers|Item Markers 兼容性|Совместимость с Item Markers|Item Markers互換性|Item Markers 호환성
Fit Item Markers to NUE equipment slots. Requires Item Markers. Takes effect after restarting MeitouClient.|Passt Item Markers an NUE-Ausrüstungsplätze an. Benötigt Item Markers und einen Neustart von MeitouClient.|Adapte Item Markers aux emplacements de NUE. Nécessite Item Markers et un redémarrage de MeitouClient.|使 Item Markers 适配 NUE 装备栏。需要 Item Markers，重启 MeitouClient 后生效。|Адаптирует Item Markers к ячейкам NUE. Требует Item Markers и перезапуск MeitouClient.|Item MarkersをNUEの装備スロットに合わせます。Item Markersが必要です。再起動後に反映されます。|Item Markers를 NUE 장비 슬롯에 맞춥니다. Item Markers가 필요하며 MeitouClient 재시작 후 적용됩니다.
Timelight world map|Timelight-Weltkarte|Carte du monde Timelight|Timelight 世界地图|Карта мира Timelight|Timelightワールドマップ|Timelight 세계 지도
Show Timelight's live view in the interactive map when its embedding API is available. Takes effect after restarting MeitouClient.|Zeigt Timelights Live-Ansicht auf der interaktiven Karte, wenn die API verfügbar ist. Wirksam nach Neustart.|Affiche la vue en direct de Timelight si son API d’intégration est disponible. Prend effet après redémarrage.|Timelight 嵌入接口可用时，在交互地图上显示其实时视图。重启 MeitouClient 后生效。|Показывает вид Timelight на интерактивной карте при наличии API. Требует перезапуск MeitouClient.|埋め込みAPIが利用可能な場合にTimelightのライブ表示を地図に表示します。再起動後に反映されます。|임베딩 API를 사용할 수 있으면 지도에 Timelight 실시간 화면을 표시합니다. 재시작 후 적용됩니다.
""";
}
