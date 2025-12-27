// Simple mock database
const users = [
  { username: "admin", password: "12345" },
  { username: "puneet", password: "password" },
  { username: "testuser", password: "abc123" }
];

document.addEventListener("DOMContentLoaded", () => {
  const form = document.querySelector(".form");

  form.addEventListener("submit", (event) => {
    event.preventDefault();

    const username = document.querySelector('input[name="username"]').value.trim();
    const password = document.querySelector('input[name="password"]').value.trim();

    // Check for valid credentials
    const user = users.find(u => u.username === username && u.password === password);

    if (user) {
      alert("✅ Login successful!");
      window.location.href = "dashboard.html"; // Redirect to dashboard
    } else {
      alert("❌ Invalid username or password. Please try again.");
      window.location.href = "login.html";
    }
  });
});
