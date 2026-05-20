using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Game Specific/Character")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    [TextArea]
    public string description;
    public Sprite portraitSprite;
}
