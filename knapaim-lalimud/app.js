/* ============================================
   כנפיים ללמידה – JavaScript
   ============================================ */

// ---- NAVBAR: scroll effect + hamburger ----
const navbar = document.getElementById('navbar');
const hamburger = document.getElementById('hamburger');
const navLinks = document.getElementById('navLinks');

window.addEventListener('scroll', () => {
  navbar.classList.toggle('scrolled', window.scrollY > 20);
});

hamburger.addEventListener('click', () => {
  navLinks.classList.toggle('open');
  const isOpen = navLinks.classList.contains('open');
  hamburger.setAttribute('aria-expanded', isOpen);
});

navLinks.querySelectorAll('a').forEach(link => {
  link.addEventListener('click', () => navLinks.classList.remove('open'));
});

// ---- SMOOTH SCROLL for all anchor links ----
document.querySelectorAll('a[href^="#"]').forEach(anchor => {
  anchor.addEventListener('click', e => {
    const target = document.querySelector(anchor.getAttribute('href'));
    if (!target) return;
    e.preventDefault();
    const offset = 80;
    window.scrollTo({ top: target.offsetTop - offset, behavior: 'smooth' });
  });
});

// ---- INTERSECTION OBSERVER: fade-in on scroll ----
const observerOptions = { threshold: 0.12 };
const observer = new IntersectionObserver((entries) => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      observer.unobserve(entry.target);
    }
  });
}, observerOptions);

document.querySelectorAll(
  '.service-card, .testimonial-card, .process-step, .about-grid, .book-wrap, .contact-grid'
).forEach(el => {
  el.classList.add('fade-in');
  observer.observe(el);
});

// ---- CONTACT FORM ----
const form = document.getElementById('contactForm');
const successMsg = document.getElementById('formSuccess');

form.addEventListener('submit', async (e) => {
  e.preventDefault();
  const btn = form.querySelector('button[type="submit"]');
  btn.disabled = true;
  btn.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> שולח...';

  const data = {
    name: form.name.value,
    phone: form.phone.value,
    email: form.email.value,
    service: form.service.value,
    message: form.message.value,
  };

  /*
   * להחלפה: הכניסו כאן את ה-endpoint של EmailJS / Formspree / שרת הbee שלכם.
   *
   * דוגמה עם Formspree:
   *   const res = await fetch('https://formspree.io/f/YOUR_FORM_ID', {
   *     method: 'POST',
   *     headers: { 'Content-Type': 'application/json' },
   *     body: JSON.stringify(data)
   *   });
   *
   * דוגמה עם EmailJS:
   *   await emailjs.send('SERVICE_ID', 'TEMPLATE_ID', data, 'PUBLIC_KEY');
   */

  // Demo: simulate network delay
  await new Promise(r => setTimeout(r, 1200));

  form.reset();
  btn.disabled = false;
  btn.innerHTML = '<i class="fa-solid fa-paper-plane"></i> שלחו הודעה';
  successMsg.classList.add('show');
  setTimeout(() => successMsg.classList.remove('show'), 5000);
});

// ---- GOOGLE CALENDAR BOOKING ----
/*
 * כשתוכנסר ה-Google Calendar Appointment Scheduling:
 * 1. פתחו Google Calendar
 * 2. לחצו על "+" > "Other calendars" > "New appointment schedule"
 * 3. הגדירו שעות פנויות
 * 4. העתיקו את קישור הקביעה שיתקבל
 * 5. החליפו את "GOOGLE_CALENDAR_BOOKING_LINK" ב-index.html בקישור זה
 */

// ---- ACTIVE NAV LINK on scroll ----
const sections = document.querySelectorAll('section[id]');
const navItems = document.querySelectorAll('.nav-links a');

const navObserver = new IntersectionObserver((entries) => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      const id = entry.target.id;
      navItems.forEach(a => {
        a.classList.toggle('active', a.getAttribute('href') === `#${id}`);
      });
    }
  });
}, { rootMargin: '-40% 0px -40% 0px' });

sections.forEach(s => navObserver.observe(s));
