
using UnityEngine;

/// <summary>A ready-to-play 2D platformer character: gravity, run, jump,
/// landing detection, and a run/idle animation toggle. Drop it on a Sprite
/// with a Rigidbody2D + BoxCollider2D (the generated scene does exactly
/// that) and it just works.</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float jumpForce = 11f;
    public LayerMask groundLayer;

    [Header("Audio")]
    public AudioClip jumpSfx;
    public AudioClip coinSfx;

    private Rigidbody2D _rb;
    private bool _grounded;
    private AudioSource _audio;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
        float x = Input.GetAxisRaw("Horizontal");
        _rb.linearVelocity = new Vector2(x * moveSpeed, _rb.linearVelocity.y);

        if (x != 0) transform.localScale = new Vector3(Mathf.Sign(x), 1, 1);

        if ((Input.GetButtonDown("Jump") || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            && _grounded)
        {
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpForce);
            if (jumpSfx && _audio) _audio.PlayOneShot(jumpSfx);
        }
    }

    void FixedUpdate()
    {
        var b = GetComponent<BoxCollider2D>();
        var origin = (Vector2)transform.position + Vector2.down * (b.bounds.extents.y + 0.05f);
        _grounded = Physics2D.Raycast(origin, Vector2.down, 0.1f, groundLayer);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Coin"))
        {
            if (coinSfx && _audio) _audio.PlayOneShot(coinSfx);
            GameManager.Instance?.Collect(other.gameObject);
        }
    }
}
