﻿#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FPTBook.Data;
using FPTBook.Models;
using Microsoft.AspNetCore.Identity;
using FPTBook.Areas.Identity.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity.UI.Services;
using System.ComponentModel.DataAnnotations;

namespace FPTBook.Controllers
{
    public class BooksController : Controller
    {
        private readonly FPTBookContext _context;
        private readonly int _recordsPerPage = 20;
        private readonly int _recordsPerPages = 20;
        private readonly UserManager<FPTBookUser> _userManager;
        private readonly IEmailSender _emailSender;
        public BooksController(FPTBookContext context, UserManager<FPTBookUser> userManager, IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _emailSender = emailSender;
        }


        public async Task<IActionResult> List(int id, string searchString)
        {
            var books1 = from b in _context.Book
                         select b;

            if (!String.IsNullOrEmpty(searchString))
            {
                books1 = books1.Where(s => s.Title!.Contains(searchString) || s.Category.Contains(searchString) || s.Author.Contains(searchString));
            }
            int numberOfRecords = await books1.CountAsync();     //Count SQL
            int numberOfPages = (int)Math.Ceiling((double)numberOfRecords / _recordsPerPages);
            ViewBag.numberOfPages = numberOfPages;
            ViewBag.currentPage = id;
            List<Book> books = await books1
                .Skip(id * _recordsPerPages)  //Offset SQL
                .Take(_recordsPerPages)       //Top SQL
                .ToListAsync();
            return View(books);
        }
        public async Task<IActionResult> Email()
        {
            FPTBookUser thisUser = await _userManager.GetUserAsync(HttpContext.User);
            await _emailSender.SendEmailAsync(thisUser.Email, "Order Success!", "Your order has been successfully placed");
            return RedirectToAction("Index", "Carts");
        }

        [Authorize(Roles = "Customer")]
        public async Task<IActionResult> AddToCart(string isbn)
        {
            string thisUserId = _userManager.GetUserId(HttpContext.User);
            Cart myCart = new Cart() { UId = thisUserId, BookIsbn = isbn, Quantity = 1 };
            Cart fromDb = _context.Cart.FirstOrDefault(c => c.UId == thisUserId && c.BookIsbn == isbn);
           
            if (fromDb != null)
            {
                fromDb.Quantity++;
                _context.Update(fromDb);
                await _context.SaveChangesAsync();
            }
            else
            {
                _context.Add(myCart);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction("List");
        }


        public async Task<IActionResult> Checkout()
        {
            string thisUserId = _userManager.GetUserId(HttpContext.User);
            List<Cart> myDetailsInCart = await _context.Cart
                .Where(c => c.UId == thisUserId)
                .Include(c => c.Book)
                .ToListAsync();
            using (var transaction = _context.Database.BeginTransaction())
            {
                try
                {

                    Order myOrder = new Order();
                    myOrder.UId = thisUserId;
                    myOrder.OrderDate = DateTime.Now;
                    myOrder.Total = myDetailsInCart.Select(c => c.Book.Price * c.Quantity)
                        .Aggregate((c1, c2) => Math.Round((c1 + c2), 1));
                    _context.Add(myOrder);
                    await _context.SaveChangesAsync();
                    
                    foreach (var item in myDetailsInCart)
                    {
                        OrderDetail detail = new OrderDetail()
                        {
                            OrderId = myOrder.Id,
                            BookIsbn = item.BookIsbn,
                            Quantity = item.Quantity
                        };
                        _context.Add(detail);
                    }
                    await _context.SaveChangesAsync();


                    _context.Cart.RemoveRange(myDetailsInCart);
                    await _context.SaveChangesAsync();
                    transaction.Commit();
                }
                catch (DbUpdateException ex)
                {
                    transaction.Rollback();
                    Console.WriteLine("Error occurred in Checkout" + ex);
                }
            }
            return RedirectToAction("Email");
        }
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> Index(int id, string searchString, string sortOrder)
        {
            FPTBookUser thisUser = await _userManager.GetUserAsync(HttpContext.User);
            Store thisStore = await _context.Store.FirstOrDefaultAsync(s => s.UId == thisUser.Id);
            var userContext = _context.Book.Where(b => b.StoreId == thisStore.Id).Include(b => b.Store);
            var books1 = from b in userContext
                         select b;
            if (!String.IsNullOrEmpty(searchString))
            {
                books1 = books1.Where(s => s.Title!.Contains(searchString));
            }


            books1 = books1.OrderByDescending(s => s.createDate);
            int numberOfRecords = await books1.CountAsync();     //Count SQL
            int numberOfPages = (int)Math.Ceiling((double)numberOfRecords / _recordsPerPage);
            ViewBag.numberOfPages = numberOfPages;
            ViewBag.currentPage = id;
            List<Book> books = await books1
                .Skip(id * _recordsPerPage)  //Offset SQL
                .Take(_recordsPerPage)       //Top SQL
                .ToListAsync();
            return View(books);
        }
      
        // GET: Books/Details/5
        [AllowAnonymous]
        public async Task<IActionResult> Details(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var book = await _context.Book
                .Include(b => b.Store)
                .FirstOrDefaultAsync(m => m.Isbn == id);
            if (book == null)
            {
                return NotFound();
            }

            return View(book);
        }
        [Authorize(Roles = "Seller")]
        // GET: Books/Create
        public IActionResult Create()
        {
            ViewData["StoreId"] = new SelectList(_context.Store, "Id", "Id");
            return View();
        }

      

        // POST: Books/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [Authorize(Roles = "Seller")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(IFormFile image, [Bind("Isbn,Title,Pages,Author,Category,Price,Desc,ImgUrl,createDate")] Book book)
        {
          

            if (image != null)
            {
                string ImageName = book.Isbn + Path.GetExtension(image.FileName);

                string SavePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/Image", ImageName);
                using (var stream = new FileStream(SavePath, FileMode.Create))
                {
                    image.CopyTo(stream);
                }
                book.ImgUrl = "Image/" + ImageName;
                book.createDate= DateTime.Now;
                FPTBookUser thisUser = await _userManager.GetUserAsync(HttpContext.User);
                Store thisStore = await _context.Store.FirstOrDefaultAsync(s => s.UId == thisUser.Id);
                book.StoreId = thisStore.Id;
            }
            else
            {
                return View(book);
            }
            _context.Add(book);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
            ViewData["StoreId"] = new SelectList(_context.Store, "Id", "Id", book.StoreId);

            return View(book);

        }

        // GET: Books/Edit/5
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> Edit(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var book = await _context.Book.FindAsync(id);
            if (book == null)
            {
                return NotFound();
            }
            ViewData["StoreId"] = new SelectList(_context.Store, "Id", "Id", book.StoreId);
            return View(book);
        }

        // POST: Books/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more details, see http://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]  // connect link
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> Edit(string id, [Bind("Isbn,Title,Pages,Author,Category,Price,Desc,ImgUrl")] Book book)
        {
            if (id != book.Isbn)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                var bookToUpdate = await _context.Book.FirstOrDefaultAsync(s => s.Isbn == id);
                if (bookToUpdate == null)
                {
                    return NotFound();
                }
                bookToUpdate.Title = book.Title;
                bookToUpdate.Pages = book.Pages;
                bookToUpdate.Category = book.Category;
                bookToUpdate.Author = book.Author;
                bookToUpdate.Pages = book.Pages;
                bookToUpdate.Price = book.Price;
                bookToUpdate.Desc = book.Desc;
                try
                {
                    _context.Update(bookToUpdate);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!BookExists(book.Isbn))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["StoreId"] = new SelectList(_context.Store, "Id", "Id", book.StoreId);
            return View(book);
        }
        [Authorize(Roles = "Seller")]
        // GET: Books/Delete/5
        public async Task<IActionResult> Delete(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var book = await _context.Book
                .Include(b => b.Store)
                .FirstOrDefaultAsync(m => m.Isbn == id);
            if (book == null)
            {
                return NotFound();
            }

            return View(book);
        }

        // POST: Books/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Seller")]
        public async Task<IActionResult> DeleteConfirmed(string id)
        {
            var book = await _context.Book.FindAsync(id);
            if (book == null)
            {
                return RedirectToAction(nameof(Index));
            }
            try
            {
                _context.Book.Remove(book);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            catch (DbUpdateException ex)
            {
                ModelState.AddModelError("", "Unable to delete book " + id + ". Error is: " + ex.Message);
                return NotFound();

            }
        }

        private bool BookExists(string id)
        {
            return _context.Book.Any(e => e.Isbn == id);
        }
    }
}
