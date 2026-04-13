using VideoEditor.Domain.Entities;

namespace VideoEditor.Application.Abstractions;

/// <summary>
/// 	РЎРѕР·РґР°РµС‚ СЃС‚Р°СЂС‚РѕРІРѕРµ СЃРѕСЃС‚РѕСЏРЅРёРµ РїСЂРѕРµРєС‚Р° РґР»СЏ СЂРµРґР°РєС‚РѕСЂР°.
/// </summary>
public interface IProjectBootstrapService
{
    /// <summary>
    /// 	РЎРѕР·РґР°РµС‚ РЅРѕРІС‹Р№ РїСЂРѕРµРєС‚ СЃ Р±Р°Р·РѕРІС‹РјРё РґРѕСЂРѕР¶РєР°РјРё.
    /// </summary>
    /// <param name="projectName">РќР°Р·РІР°РЅРёРµ РїСЂРѕРµРєС‚Р°.</param>
    VideoProject CreateDefaultProject(string projectName = "New Project");
}

