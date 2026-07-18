
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Score + win for the platformer. The generated scene wires a few
/// Coins and a Goal; collecting all coins wins.</summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Gameplay")]
    public int coinsToWin = 3;
    public string winScene = "SampleScene";

    private int _collected;

    void Awake() => Instance = this;

    public void Collect(GameObject coin)
    {
        _collected++;
        Destroy(coin);
        Debug.Log($"Coin {_collected}/{coinsToWin}");
        if (_collected >= coinsToWin) Win();
    }

    public void Win()
    {
        Debug.Log("You win! Collect coins or reach the goal.");
        SceneManager.LoadScene(winScene);
    }
}
