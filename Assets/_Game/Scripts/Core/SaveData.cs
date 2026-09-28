using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GDD 19항 저장 데이터. JsonUtility 로 직렬화되므로 전부 public 필드 + [Serializable] 이어야 한다.
/// 아이템은 참조가 아니라 id 문자열로 저장한다 — 에셋 참조는 파일로 나갈 수 없다.
/// </summary>
[Serializable]
public class SaveData
{
    public int version = 1;

    // GDD 19항: 현재 날짜·시간
    public int day = 1;
    public float minuteOfDay;

    // GDD 19항: 플레이어 위치/상태
    public Vector3 playerPosition;
    public float playerYaw;
    public float hp;
    public float stamina;
    public float hunger;
    public float thirst;
    public float fatigue;

    /// <summary>부위 6개의 부상 등급. 인덱스 = (int)BodyPart.</summary>
    public int[] injuries = new int[PlayerInjuries.PartCount];
    /// <summary>부위 6개의 출혈 여부. 이전 세이브에 없으면 모두 false 다.</summary>
    public bool[] bleeding = new bool[PlayerInjuries.PartCount];

    // GDD 19항: 인벤토리 / 장비
    public List<StackSave> inventory = new List<StackSave>();
    public List<EquipSave> equipment = new List<EquipSave>();

    /// <summary>소모품 퀵슬롯에 등록된 아이템 id. 무기 칸은 equipment 로 복원된다.</summary>
    public string[] quickSlots = new string[PlayerQuickSlots.ConsumableSlotCount];

    // GDD 19항: 월드에 떨어뜨린 아이템 ("중요" 로 명시된 항목)
    public List<WorldItemSave> worldItems = new List<WorldItemSave>();

    /// <summary>GDD 25항 저장 목록의 "컨테이너 내용물".</summary>
    public List<ContainerSave> containers = new List<ContainerSave>();

    // GDD 19항: 적 상태. 내 좀비가 든 소지품도 여기 들어간다 (GDD 8항 회수 루프)
    public List<ZombieSave> zombies = new List<ZombieSave>();
}

/// <summary>격자 좌표까지 저장해야 로드 후 배치가 그대로 복원된다 (GDD 11항).</summary>
[Serializable]
public class StackSave
{
    public string id;
    public int quantity;
    public int x;
    public int y;

    /// <summary>GDD 11항 내구도. -1 이면 최대치.</summary>
    public int durability = -1;

    /// <summary>GDD 4항 장전된 탄. -1 이면 꽉 찬 상태로 복원한다.</summary>
    public int loadedAmmo = -1;
}

[Serializable]
public class EquipSave
{
    public int slot;
    public string id;
    public int durability = -1;
    public int loadedAmmo = -1;
}

[Serializable]
public class WorldItemSave
{
    public string id;
    public int quantity;
    public Vector3 position;
    public int durability = -1;
    public int loadedAmmo = -1;
}

[Serializable]
public class ContainerSave
{
    /// <summary>이름 + 위치. 컨테이너는 월드에 고정돼 있어 이 조합으로 다시 찾는다.</summary>
    public string key = string.Empty;
    public List<StackSave> contents = new List<StackSave>();
}

[Serializable]
public class ZombieSave
{
    /// <summary>GDD 16항 좀비 종류 id. 비어 있으면 기본 좀비.</summary>
    public string typeId = string.Empty;

    public Vector3 position;
    public float yaw;
    public float health;
    public bool isPlayerCorpse;
    public List<StackSave> carried = new List<StackSave>();
}
