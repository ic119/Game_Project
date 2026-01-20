using UnityEngine;

[DisallowMultipleComponent]
public class MonsterHealth : MonoBehaviour, IDamageable
{
	[SerializeField, Min(1f)] private float maxHealth = 100f;
	[SerializeField] private bool resetHealthOnEnable = true;

	private float currentHealth;
	private Monster monster;

	private void Awake()
	{
		monster = GetComponent<Monster>();
		currentHealth = Mathf.Max(1f, maxHealth);
	}

	private void OnEnable()
	{
		if (resetHealthOnEnable)
		{
			currentHealth = Mathf.Max(1f, maxHealth);
		}
	}

	public void ApplyDamage(float amount)
	{
		float damage = Mathf.Abs(amount);
		currentHealth -= damage;
		if (currentHealth <= 0f)
		{
			Die();
		}
	}

	public void Heal(float amount)
	{
		float heal = Mathf.Abs(amount);
		currentHealth = Mathf.Min(maxHealth, currentHealth + heal);
	}

	private void Die()
	{
		if (monster != null)
		{
			monster.Kill();
		}
		else
		{
			gameObject.SetActive(false);
		}
	}

	public float GetHealth() => currentHealth;
	public float GetMaxHealth() => maxHealth;
	public float GetHealthRatio() => currentHealth / Mathf.Max(1f, maxHealth);
}

