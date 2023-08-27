using System;
using System.Buffers;

namespace Cube.Timer
{
    internal sealed class TaskList : IDisposable
    {
        // Maintains a singly linked list to store the task entries.
        // 1) the new-task will be added to the head.
        // 2) Looping over the list:
        //          a) check the task which remaining rounds <=0, then remove it.       
        //          b) otherwise decrease the remaining rounds.
        // 3. Every task entry is checked by every loop, so we don't need a doubly-linked-list, a singly-linked-list is good enough.
        // 4. We use the ArrayPool to gather expired tasks which are removed from the list, to prevent GC.

        private TaskEntry head;

        private int expiring = 0;
        private int total = 0;

        public void AddTask(TaskEntry entry)
        {
            if (head == null)
            {
                head = entry;
            }
            else
            {
                entry.Next = head;
                head = entry;
            }

            total++;

            if (entry.RemainingRounds <= 0)
            {
                expiring++;
            }
        }

        public (int total, TaskEntry[] expiredTasks, int totalNotices) RemoveExpiredTasks()
        {
            var rmTotal = expiring;
            var rmArray = ArrayPool<TaskEntry>.Shared.Rent(expiring);

            expiring = 0; // reset
            var rmNotices = 0;

            var current = head;
            var prev = head;
            var idx = 0;
            while (current != null)
            {
                TaskEntry next = current.Next;
                if (current.RemainingRounds <= 0)
                {
                    if (current == head)
                    {
                        head = current.Next;
                        prev = current.Next;
                    }
                    else
                    {
                        prev.Next = current.Next;
                    }

                    rmArray[idx++] = current;
                    if (current.TimerTaskHandle.Notice != null)
                    {
                        rmNotices++;
                    }
                }
                else
                {
                    prev = current;
                    current.DecreaseRemainingRounds();
                    if (current.RemainingRounds <= 0)
                    {
                        expiring++;
                    }
                }

                current = next;
            }

            total -= rmTotal;

            return (rmTotal, rmArray, rmNotices);
        }

        public (int total, TimerTaskHandle[] handles) RemoveAllTasks()
        {
            var arr = ArrayPool<TimerTaskHandle>.Shared.Rent(total);
            int idx = 0;
            var current = head;
            while (current != null)
            {
                var next = current.Next;

                arr[idx++] = current.TimerTaskHandle;

                current = next;
            }

            head = null;
            total = 0;
            expiring = 0;

            return (total, arr);
        }

        public void Dispose()
        {
            head = null;
        }
    }
}